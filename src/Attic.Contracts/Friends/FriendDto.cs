namespace Attic.Contracts.Friends;

public sealed record FriendDto(Guid UserId, string Username, DateTimeOffset FriendsSince);
