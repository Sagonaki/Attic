using System.Collections.Concurrent;

namespace Attic.Infrastructure.UnreadCounts;

public sealed class InMemoryUnreadCountStore : IUnreadCountStore
{
    private readonly ConcurrentDictionary<(Guid, Guid), long> _counts = new();

    public Task<long> IncrementAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var newValue = _counts.AddOrUpdate((userId, channelId), 1L, (_, old) => Interlocked.Increment(ref old));
        return Task.FromResult(newValue);
    }

    public Task ResetAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        _counts[(userId, channelId)] = 0L;
        return Task.CompletedTask;
    }

    public Task<long?> TryGetAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        long? result = _counts.TryGetValue((userId, channelId), out var v) ? v : null;
        return Task.FromResult(result);
    }

    public Task SetAsync(Guid userId, Guid channelId, long value, CancellationToken ct)
    {
        _counts[(userId, channelId)] = value;
        return Task.CompletedTask;
    }

    public Task<long[]> IncrementManyAsync(IReadOnlyList<Guid> userIds, Guid channelId, CancellationToken ct)
    {
        var results = new long[userIds.Count];
        for (int i = 0; i < userIds.Count; i++)
        {
            var newValue = _counts.AddOrUpdate((userIds[i], channelId), 1L, (_, old) => Interlocked.Increment(ref old));
            results[i] = newValue;
        }
        return Task.FromResult(results);
    }
}
