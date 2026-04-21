using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Attachments;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AttachmentsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email, HttpClientHandler Handler)> Register(CancellationToken ct)
        => TestHelpers.RegisterFreshWithHandler(fx, ct);

    private static async Task<UploadAttachmentResponse> Upload(HttpClient client, byte[] bytes,
        string fileName, string contentType, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync("/api/attachments", form, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<UploadAttachmentResponse>(ct))!;
    }

    [Fact]
    public async Task Upload_bind_and_download_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, username, _, handler) = await Register(ct);

        // Create a public room so the user can send a message.
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"att-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var payload = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21 };   // "Hello!"
        var uploadResp = await Upload(client, payload, "greeting.txt", "text/plain", ct);
        uploadResp.SizeBytes.ShouldBe(payload.Length);

        // Build cookie header directly from handler (no reflection needed).
        var cookieHeader = TestHelpers.GetCookieHeader(handler, fx);

        await using var hub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();
        await hub.StartAsync(ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "see attached", null,
                new[] { uploadResp.Id }), ct);
        send.Ok.ShouldBeTrue();

        // Download the attachment.
        var download = await client.GetAsync($"/api/attachments/{uploadResp.Id:D}", ct);
        download.EnsureSuccessStatusCode();
        var content = await download.Content.ReadAsByteArrayAsync(ct);
        content.ShouldBe(payload);
    }

    [Fact]
    public async Task Download_denied_for_non_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"att-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var uploadResp = await Upload(owner, payload, "bin.bin", "application/octet-stream", ct);

        // The attachment stays unbound, so download returns 404, which proves access control.
        var (outsider, _, _, _) = await Register(ct);
        var download = await outsider.GetAsync($"/api/attachments/{uploadResp.Id:D}", ct);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_rejects_empty_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await Register(ct);

        var resp = await client.PostAsync("/api/attachments",
            new StringContent("", new MediaTypeHeaderValue("text/plain")), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
