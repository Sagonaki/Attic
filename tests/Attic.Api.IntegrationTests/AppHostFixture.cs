using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Attic.Api.IntegrationTests;

public sealed class AppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = default!;
    public HttpClient ApiClient { get; private set; } = default!;
    public string HubUrl { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Attic_AppHost>();
        App = await appHost.BuildAsync();
        await App.StartAsync();

        ApiClient = App.CreateHttpClient("api");

        // Ensure BaseAddress is populated. CreateHttpClient should set it via Aspire service discovery.
        if (ApiClient.BaseAddress is null)
        {
            var endpoint = App.GetEndpoint("api", "http");
            ApiClient.BaseAddress = endpoint;
        }

        var hubBase = ApiClient.BaseAddress!;
        HubUrl = new Uri(hubBase, "/hub").ToString();

        // Wait for the API resource to reach Running state.
        await App.ResourceNotifications
            .WaitForResourceAsync("api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(3));

        // Poll until the API is actually accepting TCP connections.
        // KnownResourceStates.Running fires when the container starts, not when
        // the ASP.NET process inside is bound and listening.
        using var warmup = new HttpClient
        {
            BaseAddress = ApiClient.BaseAddress,
            Timeout = TimeSpan.FromSeconds(5),   // fail fast so we retry quickly
        };
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        var ready = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var resp = await warmup.GetAsync("/api/auth/me");
                // Any HTTP response (including 401) means the API is up.
                _ = resp;
                ready = true;
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        if (!ready)
            throw new InvalidOperationException(
                "API did not respond within 3 minutes after reaching Running state.");
    }

    public async ValueTask DisposeAsync()
    {
        ApiClient.Dispose();
        await App.DisposeAsync();
    }
}

[CollectionDefinition(nameof(AppHostCollection))]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture> { }
