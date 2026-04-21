using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class SessionTokenTests
{
    [Fact]
    public void Generate_produces_nonempty_token_part_and_matching_hash()
    {
        var (tokenPart, hash) = SessionToken.Generate();
        tokenPart.ShouldNotBeNullOrWhiteSpace();
        hash.ShouldNotBeNullOrWhiteSpace();
        SessionToken.Verify(hash, tokenPart).ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_token()
    {
        var (_, hash) = SessionToken.Generate();
        SessionToken.Verify(hash, "not-the-real-token").ShouldBeFalse();
    }

    [Fact]
    public void FormatCookie_then_ParseCookie_round_trips()
    {
        var sid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var token = "abc.def-ghi_jkl";
        var cookie = SessionToken.FormatCookie(sid, token);

        var parsed = SessionToken.ParseCookie(cookie);
        parsed.ShouldNotBeNull();
        parsed!.Value.SessionId.ShouldBe(sid);
        parsed.Value.TokenPart.ShouldBe(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData(".startswithdot")]
    [InlineData("endswithdot.")]
    [InlineData("not-a-guid.tokenpart")]
    public void ParseCookie_returns_null_for_invalid_input(string? cookie)
    {
        SessionToken.ParseCookie(cookie).ShouldBeNull();
    }

    [Fact]
    public void Verify_uses_fixed_time_comparison_and_handles_different_lengths()
    {
        var (tokenPart, hash) = SessionToken.Generate();
        SessionToken.Verify(hash, tokenPart + "x").ShouldBeFalse();
        SessionToken.Verify("not-a-valid-hash", tokenPart).ShouldBeFalse();
    }
}
