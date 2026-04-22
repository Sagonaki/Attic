using System.Net.Http.Json;
using Attic.Contracts.Auth;

namespace Attic.Api.IntegrationTests;

internal static class TestHelpers
{
    public static async Task<(HttpClient Client, string Username, string Email)> RegisterFresh(AppHostFixture fx, CancellationToken ct)
    {
        var (client, username, email, _) = await RegisterFreshWithHandler(fx, ct);
        return (client, username, email);
    }

    public static async Task<(HttpClient Client, string Username, string Email, HttpClientHandler Handler)> RegisterFreshWithHandler(AppHostFixture fx, CancellationToken ct)
    {
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };
        // Unique User-Agent per test client so the auth rate limiter (partitioned
        // by IP + UA) gives each fresh client its own 5/min bucket. Without this,
        // all tests collide on the shared loopback IP + default .NET HttpClient UA
        // and blow the auth limit across the full suite.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AtticIntegrationTest/{Guid.NewGuid():N}");

        var email = $"u-{Guid.NewGuid():N}@example.com";
        var username = $"u{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        return (client, username, email, handler);
    }

    public static string GetCookieHeader(HttpClientHandler handler, AppHostFixture fx)
        => string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
}
