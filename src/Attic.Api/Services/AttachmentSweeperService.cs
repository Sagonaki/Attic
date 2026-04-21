using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class AttachmentSweeperService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AttachmentSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();

                var cutoff = clock.UtcNow - OrphanAge;
                var orphans = await db.Attachments
                    .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var a in orphans)
                {
                    db.Attachments.Remove(a);
                    // Defer the ref-counted unlink to StorageSweeperService — save first, unlink after.
                }
                await db.SaveChangesAsync(stoppingToken);

                foreach (var a in orphans)
                {
                    var stillReferenced = await db.Attachments
                        .AnyAsync(x => x.StoragePath == a.StoragePath, stoppingToken);
                    if (!stillReferenced) storage.Delete(a.StoragePath);
                }

                if (orphans.Count > 0)
                    logger.LogInformation("Swept {Count} orphan attachments.", orphans.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attachment sweeper iteration failed.");
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
