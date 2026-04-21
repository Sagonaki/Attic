using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class FriendshipTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_orders_ids_canonically()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f1 = Friendship.Create(a, b, T0);
        f1.UserAId.ShouldBe(a);
        f1.UserBId.ShouldBe(b);

        var f2 = Friendship.Create(b, a, T0);
        f2.UserAId.ShouldBe(a);
        f2.UserBId.ShouldBe(b);
    }

    [Fact]
    public void Create_rejects_identical_ids()
    {
        var u = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var act = () => Friendship.Create(u, u, T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("userBId");
    }

    [Fact]
    public void Involves_returns_true_for_either_participant()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f = Friendship.Create(a, b, T0);
        f.Involves(a).ShouldBeTrue();
        f.Involves(b).ShouldBeTrue();
        f.Involves(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void Other_returns_the_other_participant()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f = Friendship.Create(a, b, T0);
        f.Other(a).ShouldBe(b);
        f.Other(b).ShouldBe(a);
    }

    [Fact]
    public void Other_rejects_non_participant()
    {
        var f = Friendship.Create(Guid.NewGuid(), Guid.NewGuid(), T0);
        Should.Throw<ArgumentException>(() => f.Other(Guid.NewGuid()));
    }
}
