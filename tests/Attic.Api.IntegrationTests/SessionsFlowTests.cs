using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Sessions;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class SessionsFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task List_sessions_returns_current()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var resp = await client.GetAsync("/api/sessions", ct);
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<List<ActiveSessionDto>>(ct))!;
        body.ShouldNotBeEmpty();
        body.ShouldContain(s => s.IsCurrent);
    }

    [Fact]
    public async Task Revoke_other_session_fires_ForceLogout_on_that_session_group()
    {
        var ct = TestContext.Current.CancellationToken;

        // Register once (first session).
        var (client1, _, email, handler1) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        // Log in as the same user in a second "browser" (second handler → second session).
        var handler2 = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client2 = new HttpClient(handler2) { BaseAddress = fx.ApiClient.BaseAddress };
        (await client2.PostAsJsonAsync("/api/auth/login",
            new Attic.Contracts.Auth.LoginRequest(email, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        // Find the second session's id via /api/sessions from client2.
        var list = (await (await client2.GetAsync("/api/sessions", ct))
            .Content.ReadFromJsonAsync<List<ActiveSessionDto>>(ct))!;
        var secondSessionId = list.First(s => s.IsCurrent).Id;

        var cookie2 = TestHelpers.GetCookieHeader(handler2, fx);
        await using var hub2 = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookie2).Build();
        await hub2.StartAsync(ct);

        var received = new TaskCompletionSource<Guid>();
        hub2.On<Guid>("ForceLogout", sid => received.TrySetResult(sid));

        // client1 revokes client2's session.
        var del = await client1.DeleteAsync($"/api/sessions/{secondSessionId:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.ShouldBe(secondSessionId);
    }
}
