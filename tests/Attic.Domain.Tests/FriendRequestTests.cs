using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class FriendRequestTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Issue_creates_pending_request()
    {
        var req = FriendRequest.Issue(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            senderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            recipientId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            text: "hello",
            now: T0);

        req.Status.ShouldBe(FriendRequestStatus.Pending);
        req.Text.ShouldBe("hello");
        req.CreatedAt.ShouldBe(T0);
        req.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_self_request()
    {
        var self = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var act = () => FriendRequest.Issue(Guid.NewGuid(), senderId: self, recipientId: self, text: null, now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("recipientId");
    }

    [Fact]
    public void Issue_trims_and_nulls_empty_text()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text: "   ", now: T0);
        req.Text.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_text_over_500_chars()
    {
        var act = () => FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            text: new string('x', 501), now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("text");
    }

    [Fact]
    public void Accept_marks_accepted_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Accept(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Accepted);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Decline_marks_declined_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Decline(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Declined);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Cancel_marks_cancelled_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Cancel(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Cancelled);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Accept_rejects_non_pending()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Accept(T0.AddMinutes(5));
        Should.Throw<InvalidOperationException>(() => req.Accept(T0.AddMinutes(10)));
    }
}
