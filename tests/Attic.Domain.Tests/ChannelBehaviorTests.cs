using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelBehaviorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    private static Channel NewRoom(ChannelKind kind = ChannelKind.Public) =>
        Channel.CreateRoom(Guid.NewGuid(), kind, "orig", "orig desc", Guid.NewGuid(), T0);

    [Fact]
    public void Rename_trims_and_sets_new_name()
    {
        var c = NewRoom();
        c.Rename("  new-name  ", T0.AddMinutes(1));
        c.Name.ShouldBe("new-name");
        c.UpdatedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_blank(string bad)
    {
        var c = NewRoom();
        Should.Throw<ArgumentException>(() => c.Rename(bad, T0.AddMinutes(1))).ParamName.ShouldBe("name");
    }

    [Fact]
    public void Rename_rejects_on_personal_channel()
    {
        var personal = Channel.CreatePersonal(Guid.NewGuid(), T0);
        Should.Throw<InvalidOperationException>(() => personal.Rename("x", T0.AddMinutes(1)));
    }

    [Fact]
    public void UpdateDescription_trims_and_sets()
    {
        var c = NewRoom();
        c.UpdateDescription("  new desc  ", T0.AddMinutes(1));
        c.Description.ShouldBe("new desc");
        c.UpdatedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void UpdateDescription_accepts_null_to_clear()
    {
        var c = NewRoom();
        c.UpdateDescription(null, T0.AddMinutes(1));
        c.Description.ShouldBeNull();
    }
}
