using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class StorageSweeperService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<StorageSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();

                // Candidate attachments: bound to a soft-deleted message (past grace period).
                // We query with filters off so we see deleted messages.
                var grace = clock.UtcNow - TimeSpan.FromMinutes(10);
                var candidates = await db.Attachments.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => a.MessageId != null &&
                                db.Messages.IgnoreQueryFilters()
                                    .Any(m => m.Id == a.MessageId && m.DeletedAt != null && m.DeletedAt < grace))
                    .ToListAsync(stoppingToken);

                foreach (var a in candidates)
                {
                    // If no live (non-deleted-message) attachment shares this path, unlink.
                    var stillReferenced = await db.Attachments.IgnoreQueryFilters()
                        .AnyAsync(x => x.Id != a.Id && x.StoragePath == a.StoragePath &&
                                       db.Messages.IgnoreQueryFilters()
                                           .Any(m => m.Id == x.MessageId && m.DeletedAt == null),
                                  stoppingToken);
                    if (!stillReferenced) storage.Delete(a.StoragePath);
                }

                if (candidates.Count > 0)
                    logger.LogInformation("Storage sweeper checked {Count} candidates.", candidates.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Storage sweeper iteration failed.");
            }
        }
    }
}
