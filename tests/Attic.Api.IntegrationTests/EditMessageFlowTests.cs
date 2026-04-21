using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class EditMessageFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email, HttpClientHandler Handler)> Register(CancellationToken ct)
        => TestHelpers.RegisterFreshWithHandler(fx, ct);

    private static async Task<HubConnection> ConnectHub(AppHostFixture fx, HttpClientHandler handler, CancellationToken ct)
    {
        var cookieHeader = TestHelpers.GetCookieHeader(handler, fx);
        var conn = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader).Build();
        await conn.StartAsync(ct);
        return conn;
    }

    [Fact]
    public async Task Author_edits_own_message_and_MessageEdited_fires()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, handler) = await Register(ct);

        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"edit-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var hub = await ConnectHub(fx, handler, ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var edited = new TaskCompletionSource<(long id, string content, DateTimeOffset updatedAt)>();
        hub.On<Guid, long, string, DateTimeOffset>("MessageEdited", (_, id, content, at) =>
            edited.TrySetResult((id, content, at)));

        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "before", null), ct);
        var messageId = send.ServerId!.Value;

        var edit = await hub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, "after"), ct);
        edit.Ok.ShouldBeTrue();
        edit.UpdatedAt.ShouldNotBeNull();

        var evt = await edited.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.id.ShouldBe(messageId);
        evt.content.ShouldBe("after");
    }

    [Fact]
    public async Task Non_author_cannot_edit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (author, _, _, authorHandler) = await Register(ct);
        var create = await author.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"na-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var authorHub = await ConnectHub(fx, authorHandler, ct);
        await authorHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await authorHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "mine", null), ct);
        var messageId = send.ServerId!.Value;

        var (outsider, _, _, outsiderHandler) = await Register(ct);
        (await outsider.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();
        await using var outsiderHub = await ConnectHub(fx, outsiderHandler, ct);
        await outsiderHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var edit = await outsiderHub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, "hijacked"), ct);
        edit.Ok.ShouldBeFalse();
        edit.Error.ShouldBe("NotAuthor");
    }

    [Fact]
    public async Task Edit_rejects_empty_content()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, handler) = await Register(ct);
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ee-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var hub = await ConnectHub(fx, handler, ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hello", null), ct);
        var messageId = send.ServerId!.Value;

        var edit = await hub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, ""), ct);
        edit.Ok.ShouldBeFalse();
        edit.Error.ShouldBe("empty_content");
    }
}
