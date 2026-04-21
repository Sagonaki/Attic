namespace Attic.Infrastructure.Presence;

public interface IPresenceStore
{
    /// <summary>Upsert a tab's state. Refreshes the per-user TTL.</summary>
    Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct);

    /// <summary>Remove a tab from the user's live set (called from OnDisconnectedAsync).</summary>
    Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct);

    /// <summary>Load every live tab state for the given user.</summary>
    Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Return and clear the set of userIds that have had heartbeats since the last call.
    /// Used by <c>PresenceHostedService</c> to limit recomputation scope.
    /// </summary>
    Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct);
}
