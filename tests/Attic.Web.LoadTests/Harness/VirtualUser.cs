using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Attic.Web.LoadTests.Harness;

public sealed class VirtualUser : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private HubConnection? _hub;

    public string Email { get; }
    public string Username { get; }
    public string Password { get; }
    public Guid? ChannelId { get; private set; }
    public int MessagesReceived { get; private set; }

    private VirtualUser(HttpClient http, HttpClientHandler handler, string email, string username, string password)
    {
        _http = http;
        _handler = handler;
        Email = email;
        Username = username;
        Password = password;
    }

    public static async Task<VirtualUser> RegisterAsync(ChatScenarioOptions options, int index, CancellationToken ct)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = options.IgnoreHttpsErrors ? (_, _, _, _) => true : null,
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.ApiBaseUrl) };

        var id = Guid.NewGuid().ToString("N")[..8];
        var email = $"load-{id}@example.test";
        var username = $"lu{index:D4}{id[..4]}";
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, options.Password), ct);
        resp.EnsureSuccessStatusCode();

        return new VirtualUser(client, handler, email, username, options.Password);
    }

    public async Task ConnectHubAsync(string hubBaseUrl, CancellationToken ct)
    {
        if (_hub is not null) return;

        var cookieHeader = string.Join("; ",
            _handler.CookieContainer.GetCookies(_http.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(hubBaseUrl), "/hub"), opts =>
            {
                opts.Headers["Cookie"] = cookieHeader;
                opts.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol()
            .Build();

        _hub.On<MessageDto>("MessageCreated", _ => { MessagesReceived++; });

        await _hub.StartAsync(ct);
    }

    public async Task EnsureInChannelAsync(string channelName, CancellationToken ct)
    {
        if (ChannelId is not null) return;

        // Try to find an existing public channel with this name; if missing, create it.
        var catalog = await _http.GetFromJsonAsync<Attic.Contracts.Common.PagedResult<CatalogItem>>(
            $"/api/channels/public?search={Uri.EscapeDataString(channelName)}&limit=50", ct);
        var match = catalog?.Items.FirstOrDefault(c => c.Name == channelName);

        if (match is null)
        {
            var created = await _http.PostAsJsonAsync("/api/channels",
                new CreateChannelRequest(channelName, "Load test shared room", "public"), ct);
            if (created.StatusCode == HttpStatusCode.Conflict)
            {
                // Race: another virtual user created it. Re-query.
                catalog = await _http.GetFromJsonAsync<Attic.Contracts.Common.PagedResult<CatalogItem>>(
                    $"/api/channels/public?search={Uri.EscapeDataString(channelName)}&limit=50", ct);
                match = catalog?.Items.First(c => c.Name == channelName);
                ChannelId = match!.Id;
            }
            else
            {
                created.EnsureSuccessStatusCode();
                var details = (await created.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
                ChannelId = details.Id;
            }
        }
        else
        {
            ChannelId = match.Id;
            // Join if not already a member.
            await _http.PostAsync($"/api/channels/{ChannelId:D}/join", content: null, ct);
        }

        await _hub!.InvokeAsync("SubscribeToChannel", ChannelId, ct);
    }

    public async Task SendMessageAsync(string content, CancellationToken ct)
    {
        if (_hub is null || ChannelId is null) throw new InvalidOperationException("User not ready.");
        await _hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(ChannelId.Value, Guid.NewGuid(), content, null, null), ct);
    }

    public async Task HeartbeatAsync(string state, CancellationToken ct)
    {
        if (_hub is null) return;
        await _hub.InvokeAsync("Heartbeat", state, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _http.Dispose();
    }

    // Local shape-matching for the catalog response (Attic.Contracts doesn't expose a PublicCatalogItem DTO directly).
    private sealed record CatalogItem(Guid Id, string Name, string? Description, int MemberCount);
}
