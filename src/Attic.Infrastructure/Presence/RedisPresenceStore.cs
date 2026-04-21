using StackExchange.Redis;

namespace Attic.Infrastructure.Presence;

public sealed class RedisPresenceStore(IConnectionMultiplexer connection) : IPresenceStore
{
    private static readonly TimeSpan HashTtl = TimeSpan.FromSeconds(90);
    private const string DirtyKey = "presence:dirty";

    private static string HashKey(Guid userId) => $"presence:user:{userId:D}";

    public async Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var key = HashKey(userId);
        var encoded = $"{(int)state}|{nowEpochMs}";
        var tx = db.CreateTransaction();
        _ = tx.HashSetAsync(key, connectionId, encoded);
        _ = tx.KeyExpireAsync(key, HashTtl);
        _ = tx.SetAddAsync(DirtyKey, userId.ToString("D"));
        await tx.ExecuteAsync();
    }

    public async Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var key = HashKey(userId);
        await db.HashDeleteAsync(key, connectionId);
        await db.SetAddAsync(DirtyKey, userId.ToString("D"));
    }

    public async Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var entries = await db.HashGetAllAsync(HashKey(userId));
        var tabs = new List<TabHeartbeat>(entries.Length);
        foreach (var e in entries)
        {
            var parts = e.Value.ToString().Split('|');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var s)) continue;
            if (!long.TryParse(parts[1], out var at)) continue;
            tabs.Add(new TabHeartbeat(e.Name!, (TabState)s, at));
        }
        return tabs;
    }

    public async Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var members = await db.SetMembersAsync(DirtyKey);
        if (members.Length == 0) return Array.Empty<Guid>();
        await db.KeyDeleteAsync(DirtyKey);
        var result = new List<Guid>(members.Length);
        foreach (var m in members)
            if (Guid.TryParse(m.ToString(), out var id)) result.Add(id);
        return result;
    }
}
