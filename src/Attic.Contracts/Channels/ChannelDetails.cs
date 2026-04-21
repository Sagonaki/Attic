namespace Attic.Contracts.Channels;

public sealed record ChannelDetails(
    Guid Id,
    string Kind,
    string? Name,
    string? Description,
    Guid? OwnerId,
    DateTimeOffset CreatedAt,
    int MemberCount);
