using System.Collections.Concurrent;

namespace Attic.Infrastructure.Presence;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, TabHeartbeat>> _tabs = new();
    private readonly ConcurrentDictionary<Guid, byte> _dirty = new();

    public Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct)
    {
        var map = _tabs.GetOrAdd(userId, _ => new ConcurrentDictionary<string, TabHeartbeat>());
        map[connectionId] = new TabHeartbeat(connectionId, state, nowEpochMs);
        _dirty[userId] = 0;
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        if (_tabs.TryGetValue(userId, out var map)) map.TryRemove(connectionId, out _);
        _dirty[userId] = 0;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct)
    {
        IReadOnlyList<TabHeartbeat> result = _tabs.TryGetValue(userId, out var map)
            ? map.Values.ToArray()
            : Array.Empty<TabHeartbeat>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> result = _dirty.Keys.ToArray();
        _dirty.Clear();
        return Task.FromResult(result);
    }
}
