namespace Attic.Infrastructure.Audit;

public sealed class AuditLogContext
{
    private readonly List<PendingEntry> _pending = new();
    public IReadOnlyList<PendingEntry> Drain()
    {
        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    public void Add(
        string action,
        Guid actorUserId,
        Guid? targetChannelId = null,
        Guid? targetUserId = null,
        long? targetMessageId = null,
        string? dataJson = null)
    {
        _pending.Add(new PendingEntry(action, actorUserId, targetChannelId, targetUserId, targetMessageId, dataJson));
    }

    public readonly record struct PendingEntry(
        string Action,
        Guid ActorUserId,
        Guid? TargetChannelId,
        Guid? TargetUserId,
        long? TargetMessageId,
        string? DataJson);
}
