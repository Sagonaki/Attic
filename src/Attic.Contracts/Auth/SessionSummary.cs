namespace Attic.Contracts.Auth;

public sealed record SessionSummary(Guid Id, string UserAgent, string Ip, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);
