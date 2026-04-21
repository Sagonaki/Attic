using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class SessionTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_sets_expiration_30_days_from_creation()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.ExpiresAt.ShouldBe(T0.AddDays(30));
        s.CreatedAt.ShouldBe(T0);
        s.LastSeenAt.ShouldBe(T0);
        s.RevokedAt.ShouldBeNull();
        s.UserAgent.ShouldBe("ua");
        s.Ip.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void IsValidAt_returns_false_when_revoked()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.Revoke(T0.AddMinutes(5));
        s.IsValidAt(T0.AddMinutes(6)).ShouldBeFalse();
    }

    [Fact]
    public void IsValidAt_returns_false_when_expired()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.IsValidAt(T0.AddDays(31)).ShouldBeFalse();
    }

    [Fact]
    public void IsValidAt_returns_true_inside_window_and_not_revoked()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.IsValidAt(T0.AddDays(10)).ShouldBeTrue();
    }

    [Fact]
    public void Touch_extends_ExpiresAt_and_updates_LastSeenAt()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.Touch(T0.AddDays(5));
        s.LastSeenAt.ShouldBe(T0.AddDays(5));
        s.ExpiresAt.ShouldBe(T0.AddDays(35));
    }
}
