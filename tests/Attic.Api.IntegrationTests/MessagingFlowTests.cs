using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class MessagingFlowTests(AppHostFixture fx)
{
    private static readonly Guid LobbyId = Guid.Parse("11111111-1111-1111-1111-000000000001");

    [Fact]
    public async Task Send_message_over_hub_persists_and_is_readable_over_REST()
    {
        var ct = TestContext.Current.CancellationToken;

        // Fresh HttpClient so this test does not share cookies with other tests.
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"m-{Guid.NewGuid():N}@example.com";
        var username = $"m{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, username, "hunter2pw"), ct))
            .EnsureSuccessStatusCode();

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts =>
            {
                opts.Headers["Cookie"] = cookieHeader;
            })
            .Build();

        await connection.StartAsync(ct);

        var received = new TaskCompletionSource<MessageDto>();
        connection.On<MessageDto>("MessageCreated", dto => received.TrySetResult(dto));

        // Subscribe to lobby channel. SubscribeToChannel returns an object; deserialize as JsonElement.
        var sub = await connection.InvokeAsync<JsonElement>("SubscribeToChannel", LobbyId, ct);
        sub.GetProperty("ok").GetBoolean().ShouldBeTrue();

        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(LobbyId, Guid.NewGuid(), "hello world", null),
            ct);

        response.Ok.ShouldBeTrue();
        response.ServerId.ShouldNotBeNull();

        var echo = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        echo.Content.ShouldBe("hello world");
        echo.SenderUsername.ShouldBe(username);

        // REST read-back
        var get = await client.GetAsync($"/api/channels/{LobbyId:D}/messages?limit=10", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await get.Content.ReadFromJsonAsync<PagedResult<MessageDto>>(ct);
        page.ShouldNotBeNull();
        page!.Items.ShouldContain(m => m.Content == "hello world");
    }

    [Fact]
    public async Task Send_message_over_3KB_returns_ok_false_with_content_too_large()
    {
        var ct = TestContext.Current.CancellationToken;

        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"big-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"big{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();

        await connection.StartAsync(ct);
        await connection.InvokeAsync<JsonElement>("SubscribeToChannel", LobbyId, ct);

        var huge = new string('x', 3200);   // > 3072 bytes in ASCII
        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(LobbyId, Guid.NewGuid(), huge, null),
            ct);

        response.Ok.ShouldBeFalse();
        response.Error.ShouldBe("content_too_large");
    }
}
