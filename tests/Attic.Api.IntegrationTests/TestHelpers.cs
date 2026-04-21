using System.Net.Http.Json;
using Attic.Contracts.Auth;

namespace Attic.Api.IntegrationTests;

internal static class TestHelpers
{
    public static async Task<(HttpClient Client, string Username, string Email)> RegisterFresh(AppHostFixture fx, CancellationToken ct)
    {
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"u-{Guid.NewGuid():N}@example.com";
        var username = $"u{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        return (client, username, email);
    }
}
