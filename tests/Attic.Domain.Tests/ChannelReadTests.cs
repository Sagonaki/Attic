using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelReadTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_zero_read_marker()
    {
        var c = ChannelRead.Create(
            channelId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            userId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            lastReadMessageId: 0,
            now: T0);
        c.ChannelId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        c.UserId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        c.LastReadMessageId.ShouldBe(0);
        c.UpdatedAt.ShouldBe(T0);
    }

    [Fact]
    public void MarkRead_advances_marker_and_stamps_updatedAt()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 0, T0);
        c.MarkRead(42, T0.AddMinutes(5));
        c.LastReadMessageId.ShouldBe(42);
        c.UpdatedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void MarkRead_is_monotonic()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 100, T0);
        c.MarkRead(50, T0.AddMinutes(5));
        // Rewind rejected; remains 100.
        c.LastReadMessageId.ShouldBe(100);
    }

    [Fact]
    public void MarkRead_idempotent_on_same_id()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 100, T0);
        c.MarkRead(100, T0.AddMinutes(5));
        c.LastReadMessageId.ShouldBe(100);
        // UpdatedAt still advances (we refreshed the row).
        c.UpdatedAt.ShouldBe(T0.AddMinutes(5));
    }
}
