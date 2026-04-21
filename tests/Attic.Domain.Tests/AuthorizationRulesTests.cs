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
    public void CanJoinChannel_denies_deleted_channel()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        channel.SoftDelete(T0_J.AddMinutes(1));
        var result = AuthorizationRules.CanJoinChannel(channel, existingMember: null);
        result.Reason.ShouldBe(AuthorizationFailureReason.ChannelDeleted);
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

    [Fact]
    public void CanManageChannel_allows_admin_and_owner()
    {
        var admin = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanManageChannel(admin).Allowed.ShouldBeTrue();
        var owner = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanManageChannel(owner).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanManageChannel_denies_plain_member()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanManageChannel(member).Reason.ShouldBe(AuthorizationFailureReason.NotAdmin);
    }

    [Fact]
    public void CanManageChannel_denies_banned_admin()
    {
        var admin = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Admin, T0_J);
        admin.Ban(Guid.NewGuid(), "spam", T0_J);
        AuthorizationRules.CanManageChannel(admin).Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanDeleteChannel_allows_owner()
    {
        var channelId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var channel = Channel.CreateRoom(channelId, ChannelKind.Public, "room", null, ownerId, T0_J);
        AuthorizationRules.CanDeleteChannel(channel, actorUserId: ownerId).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanDeleteChannel_denies_non_owner()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        AuthorizationRules.CanDeleteChannel(channel, actorUserId: Guid.NewGuid()).Reason
            .ShouldBe(AuthorizationFailureReason.NotOwner);
    }

    [Fact]
    public void CanBanFromChannel_allows_admin_banning_member()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanBanFromChannel_allows_owner_banning_admin()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanBanFromChannel_denies_banning_owner()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var owner = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, owner).Reason
            .ShouldBe(AuthorizationFailureReason.OwnerCannotBeTargeted);
    }

    [Fact]
    public void CanBanFromChannel_denies_non_admin_actor()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Reason.ShouldBe(AuthorizationFailureReason.NotAdmin);
    }

    [Fact]
    public void CanUnbanFromChannel_allows_admin()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanUnbanFromChannel(actor).Allowed.ShouldBeTrue();
    }
}
