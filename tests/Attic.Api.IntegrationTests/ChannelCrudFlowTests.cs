using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class ChannelCrudFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Create_channel_returns_details_and_makes_creator_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"room-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "test", "public"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        details.ShouldNotBeNull();
        details!.Name.ShouldBe(name);
        details.Kind.ShouldBe("public");
        details.MemberCount.ShouldBe(1);
        details.OwnerId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_channel_rejects_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"dup-{Guid.NewGuid():N}"[..20];

        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct)).EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_channel_rejects_invalid_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var response = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest("ok-name", null, "personal"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiError>(ct);
        err.ShouldNotBeNull();
        err!.Code.ShouldBe("invalid_kind");
    }

    internal async Task<(HttpClient Client, string Username)> RegisterFresh(CancellationToken ct)
    {
        var (client, username, _) = await TestHelpers.RegisterFresh(fx, ct);
        return (client, username);
    }
}
