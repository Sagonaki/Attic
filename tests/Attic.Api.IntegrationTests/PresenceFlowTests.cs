using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class PresenceFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email, HttpClientHandler Handler)> RegisterFresh(CancellationToken ct)
        => TestHelpers.RegisterFreshWithHandler(fx, ct);

    [Fact]
    public async Task Friend_receives_PresenceChanged_on_heartbeat()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _, aliceHandler) = await RegisterFresh(ct);
        var (bob, bobUsername, _, bobHandler) = await RegisterFresh(ct);

        // Make them friends.
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var aliceCookie = TestHelpers.GetCookieHeader(aliceHandler, fx);
        var bobCookie = TestHelpers.GetCookieHeader(bobHandler, fx);

        await using var aliceHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = aliceCookie).Build();
        await using var bobHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = bobCookie).Build();
        await aliceHub.StartAsync(ct);
        await bobHub.StartAsync(ct);

        var received = new TaskCompletionSource<(Guid, string)>();
        bobHub.On<Guid, string>("PresenceChanged", (uid, state) => received.TrySetResult((uid, state)));

        await aliceHub.InvokeAsync("Heartbeat", "active", ct);

        // PresenceHostedService runs on a 1s tick — allow up to 5s.
        var (userId, statusCode) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        statusCode.ShouldBe("online");
    }
}
