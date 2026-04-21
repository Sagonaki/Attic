using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class UserBlockTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_blocker_blocked_pair()
    {
        var blocker = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var blocked = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var b = UserBlock.Create(blocker, blocked, T0);
        b.BlockerId.ShouldBe(blocker);
        b.BlockedId.ShouldBe(blocked);
        b.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Create_rejects_self_block()
    {
        var u = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var act = () => UserBlock.Create(u, u, T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("blockedId");
    }
}
