using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class HubErrorFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task SendMessage_returns_rate_limited_after_60_calls_per_minute()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, handler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rl-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var cookie = TestHelpers.GetCookieHeader(handler, fx);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookie).Build();
        await hub.StartAsync(ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        // 60 successful sends.
        for (int i = 0; i < 60; i++)
        {
            var resp = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
                new SendMessageRequest(channel.Id, Guid.NewGuid(), $"m{i}", null, null), ct);
            resp.Ok.ShouldBeTrue($"send #{i} should have succeeded");
        }

        // 61st triggers rate_limited.
        var denied = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "one-too-many", null, null), ct);
        denied.Ok.ShouldBeFalse();
        denied.Error.ShouldBe("rate_limited");
    }
}
