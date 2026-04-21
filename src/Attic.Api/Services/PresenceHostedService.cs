using Attic.Api.Hubs;
using Attic.Contracts.Presence;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Presence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class PresenceHostedService(
    IServiceScopeFactory scopeFactory,
    IPresenceStore store,
    IClock clock,
    ILogger<PresenceHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private const long OnlineMaxAgeMs = 30_000;
    private const long AliveMaxAgeMs = 90_000;

    private readonly Dictionary<Guid, string> _last = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                var dirty = await store.DrainDirtyAsync(stoppingToken);
                if (dirty.Count == 0) continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var broadcaster = scope.ServiceProvider.GetRequiredService<PresenceEventBroadcaster>();

                var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
                foreach (var userId in dirty)
                {
                    var tabs = await store.ListAsync(userId, stoppingToken);
                    var state = Derive(tabs, nowMs);
                    if (_last.TryGetValue(userId, out var prev) && prev == state) continue;
                    _last[userId] = state;

                    var recipients = await FindRecipientsAsync(db, userId, stoppingToken);
                    await broadcaster.PresenceChanged(recipients, userId, state);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Presence tick failed.");
            }
        }
    }

    private static string Derive(IReadOnlyList<TabHeartbeat> tabs, long nowMs)
    {
        if (tabs.Count == 0) return PresenceStateCode.Offline;
        var alive = tabs.Where(t => nowMs - t.EpochMs <= AliveMaxAgeMs).ToList();
        if (alive.Count == 0) return PresenceStateCode.Offline;
        if (alive.Any(t => t.State == TabState.Active && nowMs - t.EpochMs <= OnlineMaxAgeMs))
            return PresenceStateCode.Online;
        return PresenceStateCode.Afk;
    }

    private static async Task<IReadOnlyList<Guid>> FindRecipientsAsync(
        AtticDbContext db, Guid userId, CancellationToken ct)
    {
        // Friends of the user.
        var friendIds = await db.Friendships.AsNoTracking()
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToListAsync(ct);

        // Members of channels the user is in (excluding self).
        var sharedChannelIds = db.ChannelMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId);

        var coMemberIds = await db.ChannelMembers.AsNoTracking()
            .Where(m => sharedChannelIds.Contains(m.ChannelId) && m.UserId != userId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        return friendIds.Concat(coMemberIds).Distinct().ToList();
    }
}
