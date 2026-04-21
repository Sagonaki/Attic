namespace Attic.Contracts.Invitations;

public sealed record InvitationDto(
    Guid Id,
    Guid ChannelId,
    string ChannelName,
    Guid InviterId,
    string InviterUsername,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
