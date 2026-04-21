namespace Attic.Contracts.Friends;

public sealed record FriendRequestDto(
    Guid Id,
    Guid SenderId,
    string SenderUsername,
    Guid RecipientId,
    string RecipientUsername,
    string? Text,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
