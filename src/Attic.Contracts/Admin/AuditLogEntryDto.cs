namespace Attic.Contracts.Admin;

public sealed record AuditLogEntryDto(
    long Id,
    Guid ActorUserId,
    string Action,
    Guid? TargetChannelId,
    Guid? TargetUserId,
    long? TargetMessageId,
    string? Data,
    DateTimeOffset CreatedAt);
