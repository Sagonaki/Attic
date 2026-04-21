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
public sealed class UnreadFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Member_receives_UnreadChanged_when_other_sends()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, ownerHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var (member, _, _, memberHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ur-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        (await member.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var memberCookie = TestHelpers.GetCookieHeader(memberHandler, fx);
        await using var memberHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = memberCookie).Build();
        await memberHub.StartAsync(ct);

        var received = new TaskCompletionSource<(Guid channelId, int count)>();
        memberHub.On<Guid, int>("UnreadChanged", (cid, c) => received.TrySetResult((cid, c)));

        var ownerCookie = TestHelpers.GetCookieHeader(ownerHandler, fx);
        await using var ownerHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = ownerCookie).Build();
        await ownerHub.StartAsync(ct);
        await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        await ownerHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hi", null, null), ct);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.channelId.ShouldBe(channel.Id);
        evt.count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MarkRead_zeros_unread_and_broadcasts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, ownerHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var (member, _, _, memberHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"mr-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        (await member.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var ownerCookie = TestHelpers.GetCookieHeader(ownerHandler, fx);
        await using var ownerHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = ownerCookie).Build();
        await ownerHub.StartAsync(ct);
        await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await ownerHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hi", null, null), ct);
        var messageId = send.ServerId!.Value;

        var memberCookie = TestHelpers.GetCookieHeader(memberHandler, fx);
        await using var memberHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = memberCookie).Build();
        await memberHub.StartAsync(ct);

        var zeroed = new TaskCompletionSource<int>();
        memberHub.On<Guid, int>("UnreadChanged", (cid, c) => {
            if (cid == channel.Id && c == 0) zeroed.TrySetResult(c);
        });

        await memberHub.InvokeAsync<JsonElement>("MarkRead", channel.Id, messageId, ct);

        var c = await zeroed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        c.ShouldBe(0);
    }
}
