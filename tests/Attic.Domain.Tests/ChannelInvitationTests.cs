using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelInvitationTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Issue_creates_pending_invitation()
    {
        var inv = ChannelInvitation.Issue(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            channelId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            inviterId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            inviteeId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            now: T0);

        inv.Id.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        inv.ChannelId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        inv.InviterId.ShouldBe(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        inv.InviteeId.ShouldBe(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        inv.Status.ShouldBe(InvitationStatus.Pending);
        inv.CreatedAt.ShouldBe(T0);
        inv.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_self_invitation()
    {
        var self = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var act = () => ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), inviterId: self, inviteeId: self, now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("inviteeId");
    }

    [Fact]
    public void Accept_marks_accepted_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Accepted);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Decline_marks_declined_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Decline(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Declined);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Cancel_marks_cancelled_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Cancel(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Cancelled);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Accept_is_idempotent_on_already_accepted()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        var act = () => inv.Accept(T0.AddMinutes(10));
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void Decline_rejects_non_pending()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        Should.Throw<InvalidOperationException>(() => inv.Decline(T0.AddMinutes(10)));
    }
}
