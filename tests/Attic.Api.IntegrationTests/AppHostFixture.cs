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
    }

    public async ValueTask DisposeAsync()
    {
        ApiClient.Dispose();
        await App.DisposeAsync();
    }
}

[CollectionDefinition(nameof(AppHostCollection))]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture> { }
