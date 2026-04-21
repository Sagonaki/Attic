using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class RealtimeEventsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

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
    public async Task Banned_user_receives_RemovedFromChannel_over_hub()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rm-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var joinerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var joiner = new HttpClient(joinerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var email = $"rm-{Guid.NewGuid():N}@example.com";
        var username = $"j{Random.Shared.Next():x}";
        (await joiner.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        await using var connection = await ConnectHub(fx, joinerHandler, ct);
        var removed = new TaskCompletionSource<(Guid channelId, string reason)>();
        connection.On<Guid, string>("RemovedFromChannel", (cid, reason) => removed.TrySetResult((cid, reason)));

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Username == username).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        var evt = await removed.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.channelId.ShouldBe(channel.Id);
        evt.reason.ShouldBe("banned");
    }

    [Fact]
    public async Task Admin_delete_message_fires_MessageDeleted()
    {
        var ct = TestContext.Current.CancellationToken;

        var ownerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var ownerClient = new HttpClient(ownerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var ownerEmail = $"o-{Guid.NewGuid():N}@example.com";
        var ownerUser = $"o{Random.Shared.Next():x}";
        (await ownerClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(ownerEmail, ownerUser, "hunter2pw"), ct)).EnsureSuccessStatusCode();
        var altCreate = await ownerClient.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"md2-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var altChannel = (await altCreate.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var altPosterHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var altPoster = new HttpClient(altPosterHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await altPoster.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"p-{Guid.NewGuid():N}@example.com", $"p{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();
        (await altPoster.PostAsync($"/api/channels/{altChannel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        await using var ownerHub = await ConnectHub(fx, ownerHandler, ct);
        var deleted = new TaskCompletionSource<(Guid cid, long mid)>();
        ownerHub.On<Guid, long>("MessageDeleted", (cid, mid) => deleted.TrySetResult((cid, mid)));
        (await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", altChannel.Id, ct))
            .GetProperty("ok").GetBoolean().ShouldBeTrue();

        await using var posterHub = await ConnectHub(fx, altPosterHandler, ct);
        (await posterHub.InvokeAsync<JsonElement>("SubscribeToChannel", altChannel.Id, ct))
            .GetProperty("ok").GetBoolean().ShouldBeTrue();

        var send = await posterHub.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(altChannel.Id, Guid.NewGuid(), "delete me", null), ct);
        send.Ok.ShouldBeTrue();
        var messageId = send.ServerId!.Value;

        var del = await ownerHub.InvokeAsync<JsonElement>("DeleteMessage", messageId, ct);
        del.GetProperty("ok").GetBoolean().ShouldBeTrue();

        var evt = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.cid.ShouldBe(altChannel.Id);
        evt.mid.ShouldBe(messageId);
    }
}
