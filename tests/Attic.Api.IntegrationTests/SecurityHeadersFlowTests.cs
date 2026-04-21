using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class SecurityHeadersFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Every_response_carries_security_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fx.ApiClient.GetAsync("/api/auth/me", ct);   // returns 401, but headers still set

        resp.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        resp.Headers.GetValues("X-Content-Type-Options").First().ShouldBe("nosniff");

        resp.Headers.Contains("X-Frame-Options").ShouldBeTrue();
        resp.Headers.GetValues("X-Frame-Options").First().ShouldBe("DENY");

        resp.Headers.Contains("Referrer-Policy").ShouldBeTrue();
        resp.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
}
