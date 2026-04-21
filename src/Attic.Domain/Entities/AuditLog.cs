namespace Attic.Domain.Entities;

public sealed class AuditLog
{
    public long Id { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid? TargetChannelId { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public long? TargetMessageId { get; private set; }
    public string? Data { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid actorUserId,
        string action,
        Guid? targetChannelId,
        Guid? targetUserId,
        long? targetMessageId,
        string? dataJson,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));

        return new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            TargetChannelId = targetChannelId,
            TargetUserId = targetUserId,
            TargetMessageId = targetMessageId,
            Data = dataJson,
            CreatedAt = now
        };
    }
}
