using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class KeysetCursorTests
{
    [Fact]
    public void Encode_then_Decode_roundtrips_the_value()
    {
        var original = KeysetCursor.Encode(12345L);
        var decoded = KeysetCursor.TryDecode(original, out var value);
        decoded.ShouldBeTrue();
        value.ShouldBe(12345L);
    }

    [Fact]
    public void TryDecode_returns_false_for_null_or_empty_input()
    {
        KeysetCursor.TryDecode(null, out _).ShouldBeFalse();
        KeysetCursor.TryDecode("", out _).ShouldBeFalse();
    }

    [Fact]
    public void TryDecode_rejects_tampered_cursor()
    {
        KeysetCursor.TryDecode("!!!not-base64!!!", out _).ShouldBeFalse();
    }

    [Fact]
    public void TryDecode_rejects_negative_values()
    {
        var sneaky = KeysetCursor.Encode(-1);
        KeysetCursor.TryDecode(sneaky, out _).ShouldBeFalse();
    }
}
