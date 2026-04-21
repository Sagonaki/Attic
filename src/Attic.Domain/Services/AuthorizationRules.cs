using Attic.Domain.Entities;

namespace Attic.Domain.Services;

public static class AuthorizationRules
{
    public static AuthorizationResult CanPostInChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
        return AuthorizationResult.Ok();
    }
}
