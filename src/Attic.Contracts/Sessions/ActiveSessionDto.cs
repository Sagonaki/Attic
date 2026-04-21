namespace Attic.Contracts.Sessions;

public sealed record ActiveSessionDto(
    Guid Id,
    string UserAgent,
    string? Ip,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);
