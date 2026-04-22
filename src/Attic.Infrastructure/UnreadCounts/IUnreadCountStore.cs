namespace Attic.Infrastructure.UnreadCounts;

public interface IUnreadCountStore
{
    /// <summary>Atomically increments the unread counter for a user in a channel and returns the new value.</summary>
    Task<long> IncrementAsync(Guid userId, Guid channelId, CancellationToken ct);

    /// <summary>Resets the unread counter to 0 (used on MarkRead).</summary>
    Task ResetAsync(Guid userId, Guid channelId, CancellationToken ct);

    /// <summary>Reads the current unread count. Returns null if not cached (caller should fall back to DB).</summary>
    Task<long?> TryGetAsync(Guid userId, Guid channelId, CancellationToken ct);

    /// <summary>Seeds the cache with a value. Used after a DB-side compute falls back and we want to warm the cache.</summary>
    Task SetAsync(Guid userId, Guid channelId, long value, CancellationToken ct);

    /// <summary>Atomically increments unread counters for many users on the same channel and returns the new values.
    /// Ordering of the returned array matches the input.</summary>
    Task<long[]> IncrementManyAsync(IReadOnlyList<Guid> userIds, Guid channelId, CancellationToken ct);
}
