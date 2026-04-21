using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class RateLimitFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Login_rate_limited_after_5_attempts_in_a_minute()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };
        // Use a unique User-Agent so this test gets its own rate-limit bucket,
        // isolated from other integration tests that also call /login from loopback.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"RateLimitTest/{Guid.NewGuid():N}");

        // 5 wrong-password attempts — rate limiter allows all of them (they just 401).
        for (int i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest("nobody@example.com", "wrong"), ct);
            resp.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        // 6th in the same window → 429.
        var blocked = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@example.com", "wrong"), ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
