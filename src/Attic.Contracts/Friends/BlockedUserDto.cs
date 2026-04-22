namespace Attic.Contracts.Friends;
public sealed record BlockedUserDto(Guid UserId, string Username, DateTimeOffset BlockedAt);
