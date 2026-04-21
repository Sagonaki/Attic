using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AuthFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Register_then_Me_returns_the_new_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"alice-{Guid.NewGuid():N}@example.com";
        var username = $"alice{Random.Shared.Next():x}";

        var register = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct);
        register.StatusCode.ShouldBe(HttpStatusCode.OK);
        register.Headers.Contains("Set-Cookie").ShouldBeTrue();

        var me = await fx.ApiClient.GetAsync("/api/auth/me", ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>(ct);
        body.ShouldNotBeNull();
        body!.Username.ShouldBe(username);
        body.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"user{Random.Shared.Next():x}", "hunter2pw"), ct);
        var second = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"user{Random.Shared.Next():x}", "hunter2pw"), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"bob-{Guid.NewGuid():N}@example.com";
        var reg = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"bob{Random.Shared.Next():x}", "hunter2pw"), ct);
        reg.EnsureSuccessStatusCode();

        var bad = await fx.ApiClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "wrongpassword"), ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_session_and_Me_becomes_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"carol-{Guid.NewGuid():N}@example.com";
        await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"carol{Random.Shared.Next():x}", "hunter2pw"), ct);

        var logout = await fx.ApiClient.PostAsync("/api/auth/logout", null, ct);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var me = await fx.ApiClient.GetAsync("/api/auth/me", ct);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
