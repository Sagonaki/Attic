namespace Attic.Domain.Services;

public enum AuthorizationFailureReason
{
    None = 0,
    NotAMember,
    BannedFromChannel,
    NotFriends,
    BlockedByOrBlockingUser,
    NotAuthor,
    NotAdmin,
    NotOwner,
    OwnerCannotLeave,
    OwnerCannotBeTargeted,
    OwnerCannotBeDemoted,
    AlreadyMember,
    AlreadyInvited,
    InvitationNotFound,
    ChannelNotFound,
    ChannelDeleted,
    CannotInviteToPublic,
    CannotAutoJoinPrivate,
    DuplicateFriendRequest
}

public readonly record struct AuthorizationResult(bool Allowed, AuthorizationFailureReason Reason)
{
    public static AuthorizationResult Ok() => new(true, AuthorizationFailureReason.None);
    public static AuthorizationResult Deny(AuthorizationFailureReason reason) => new(false, reason);
}
