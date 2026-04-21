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

    private static readonly DateTimeOffset T0_J = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void CanJoinChannel_allows_public_room_when_not_a_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, existingMember: null);
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanJoinChannel_denies_already_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var member = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, member);
        result.Reason.ShouldBe(AuthorizationFailureReason.AlreadyMember);
    }

    [Fact]
    public void CanJoinChannel_denies_banned_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var member = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        member.Ban(Guid.NewGuid(), "spam", T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, member);
        result.Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanJoinChannel_denies_private_room_without_invitation()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, existingMember: null);
        result.Reason.ShouldBe(AuthorizationFailureReason.CannotAutoJoinPrivate);
    }

    [Fact]
    public void CanLeaveChannel_allows_non_owner_member()
    {
        var channelId = Guid.NewGuid();
        var member = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanLeaveChannel(member).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanLeaveChannel_denies_owner()
    {
        var channelId = Guid.NewGuid();
        var owner = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanLeaveChannel(owner).Reason.ShouldBe(AuthorizationFailureReason.OwnerCannotLeave);
    }

    [Fact]
    public void CanLeaveChannel_denies_nonmember()
    {
        AuthorizationRules.CanLeaveChannel(null).Reason.ShouldBe(AuthorizationFailureReason.NotAMember);
    }
}
