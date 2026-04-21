using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AuditLogTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_action_and_timestamp()
    {
        var entry = AuditLog.Create(
            actorUserId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            action: "channel.ban_member",
            targetChannelId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            targetUserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            targetMessageId: null,
            dataJson: """{"reason":"spam"}""",
            now: T0);

        entry.ActorUserId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        entry.Action.ShouldBe("channel.ban_member");
        entry.TargetChannelId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        entry.TargetUserId.ShouldBe(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        entry.TargetMessageId.ShouldBeNull();
        entry.Data.ShouldBe("""{"reason":"spam"}""");
        entry.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Create_rejects_empty_action()
    {
        Should.Throw<ArgumentException>(() => AuditLog.Create(
            Guid.NewGuid(), "", null, null, null, null, T0)).ParamName.ShouldBe("action");
    }

    [Fact]
    public void Create_allows_null_data()
    {
        var entry = AuditLog.Create(Guid.NewGuid(), "session.revoke", null, null, null, null, T0);
        entry.Data.ShouldBeNull();
    }
}
