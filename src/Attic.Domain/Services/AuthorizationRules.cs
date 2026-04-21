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

    public static AuthorizationResult CanManageChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
        if (member.Role == Enums.ChannelRole.Member) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAdmin);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanDeleteChannel(Channel channel, Guid actorUserId)
    {
        if (channel.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.ChannelDeleted);
        if (channel.OwnerId != actorUserId) return AuthorizationResult.Deny(AuthorizationFailureReason.NotOwner);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanBanFromChannel(ChannelMember? actor, ChannelMember? target)
    {
        var manage = CanManageChannel(actor);
        if (!manage.Allowed) return manage;
        if (target is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (target.Role == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeTargeted);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanUnbanFromChannel(ChannelMember? actor) => CanManageChannel(actor);

    public static AuthorizationResult CanChangeRole(ChannelMember? actor, ChannelMember? target, Enums.ChannelRole newRole)
    {
        var manage = CanManageChannel(actor);
        if (!manage.Allowed) return manage;
        if (target is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (target.Role == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeDemoted);
        if (newRole == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeTargeted);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanDeleteMessage(
        Message message,
        Guid actorUserId,
        ChannelMember? actorMembership,
        Enums.ChannelKind channelKind)
    {
        if (message.SenderId == actorUserId) return AuthorizationResult.Ok();
        // Room admins (not personal) can delete any message.
        if (channelKind != Enums.ChannelKind.Personal && actorMembership is not null
            && actorMembership.BannedAt is null && actorMembership.Role != Enums.ChannelRole.Member)
        {
            return AuthorizationResult.Ok();
        }
        return AuthorizationResult.Deny(AuthorizationFailureReason.NotAuthor);
    }
}
