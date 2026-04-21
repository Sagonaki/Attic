using Attic.Domain.Entities;
using Attic.Domain.Enums;

namespace Attic.Domain.Services;

public static class AuthorizationRules
{
    public static AuthorizationResult CanPostInChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanJoinChannel(Channel channel, ChannelMember? existingMember)
    {
        if (channel.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.ChannelDeleted);
        if (existingMember is not null)
        {
            if (existingMember.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
            return AuthorizationResult.Deny(AuthorizationFailureReason.AlreadyMember);
        }
        if (channel.Kind == ChannelKind.Private)
            return AuthorizationResult.Deny(AuthorizationFailureReason.CannotAutoJoinPrivate);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanLeaveChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.Role == ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotLeave);
        return AuthorizationResult.Ok();
    }
}
