namespace Attic.Contracts.Channels;

public sealed record BannedMemberSummary(
    Guid UserId,
    string Username,
    Guid BannedById,
    string? BannedByUsername,
    DateTimeOffset BannedAt,
    string? Reason);
