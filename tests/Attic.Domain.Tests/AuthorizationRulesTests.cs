using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AuthorizationRulesTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void CanPostInChannel_allows_active_member()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0);
        var result = AuthorizationRules.CanPostInChannel(member);
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanPostInChannel_blocks_banned_member_with_reason()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0);
        member.Ban(Guid.NewGuid(), "spam", T0.AddMinutes(5));

        var result = AuthorizationRules.CanPostInChannel(member);
        result.Allowed.ShouldBeFalse();
        result.Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanPostInChannel_blocks_null_membership()
    {
        var result = AuthorizationRules.CanPostInChannel(null);
        result.Allowed.ShouldBeFalse();
        result.Reason.ShouldBe(AuthorizationFailureReason.NotAMember);
    }
}
