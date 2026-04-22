using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class PasswordFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Forgot_password_returns_ok_for_unknown_email()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var resp = await client.PostAsJsonAsync("/api/auth/password/forgot",
            new ForgotPasswordRequest("nobody@example.com"), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_replaces_hash_when_user_exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, email, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        // Old password works.
        (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "hunter2pw"), ct))
            .EnsureSuccessStatusCode();

        // Trigger forgot flow — new password is logged to server console, not returned.
        (await client.PostAsJsonAsync("/api/auth/password/forgot",
            new ForgotPasswordRequest(email), ct)).EnsureSuccessStatusCode();

        // Old password no longer works.
        var failed = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "hunter2pw"), ct);
        failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var resp = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("wrong", "new-pass-123"), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_updates_hash()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, email, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var resp = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("hunter2pw", "new-pass-123"), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Old password no longer works on fresh login.
        var handler2 = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var fresh = new HttpClient(handler2) { BaseAddress = fx.ApiClient.BaseAddress };
        var oldFail = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "hunter2pw"), ct);
        oldFail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // New password works.
        var newOk = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "new-pass-123"), ct);
        newOk.EnsureSuccessStatusCode();
    }
}
