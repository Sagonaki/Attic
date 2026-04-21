using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class RealtimeFriendEventsTests(AppHostFixture fx)
{
    private static async Task<HubConnection> ConnectHub(AppHostFixture fx, HttpClientHandler handler, CancellationToken ct)
    {
        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
        var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();
        await connection.StartAsync(ct);
        return connection;
    }

    [Fact]
    public async Task FriendRequestReceived_arrives_over_hub()
    {
        var ct = TestContext.Current.CancellationToken;

        var senderHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var sender = new HttpClient(senderHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await sender.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"a-{Guid.NewGuid():N}@example.com", $"a{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var recipientHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var recipient = new HttpClient(recipientHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var recipientUsername = $"r{Random.Shared.Next():x}";
        (await recipient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"r-{Guid.NewGuid():N}@example.com", recipientUsername, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        await using var recipientHub = await ConnectHub(fx, recipientHandler, ct);
        var received = new TaskCompletionSource<FriendRequestDto>();
        recipientHub.On<FriendRequestDto>("FriendRequestReceived", dto => received.TrySetResult(dto));

        (await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, "ping"), ct)).EnsureSuccessStatusCode();

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.RecipientUsername.ShouldBe(recipientUsername);
        evt.Text.ShouldBe("ping");
    }

    [Fact]
    public async Task Blocked_event_fires_on_recipient()
    {
        var ct = TestContext.Current.CancellationToken;

        var blockerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var blocker = new HttpClient(blockerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await blocker.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"bl-{Guid.NewGuid():N}@example.com", $"bl{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var blockedHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var blocked = new HttpClient(blockedHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var blockedUsername = $"blocked{Random.Shared.Next():x}";
        (await blocked.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"b-{Guid.NewGuid():N}@example.com", blockedUsername, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var search = await blocker.GetAsync($"/api/users/search?q={blockedUsername[..4]}", ct);
        var blockedId = (await search.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!
            .First(u => u.Username == blockedUsername).Id;

        await using var blockedHub = await ConnectHub(fx, blockedHandler, ct);
        var blockEvent = new TaskCompletionSource<Guid>();
        blockedHub.On<Guid>("Blocked", blockerId => blockEvent.TrySetResult(blockerId));

        (await blocker.PostAsync($"/api/users/{blockedId:D}/block", null, ct)).EnsureSuccessStatusCode();

        var evt = await blockEvent.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.ShouldNotBe(Guid.Empty);
    }
}
