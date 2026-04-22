using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task Public_catalog_lists_created_public_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"cat-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "catalog-test", "public"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/channels/public?search={name[..8]}&limit=50", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.Items.ShouldContain(i => i.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Public_catalog_excludes_private_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var privateName = $"priv-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(privateName, null, "private"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/channels/public?search={privateName[..8]}", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task GET_channels_mine_returns_created_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"mine-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/channels/mine", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.ShouldContain(c => c.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task GET_channels_id_returns_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"det-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "detail", "public"), ct);
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var detail = await client.GetAsync($"/api/channels/{channel.Id:D}", ct);
        detail.EnsureSuccessStatusCode();
        var body = await detail.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        body.ShouldNotBeNull();
        body!.Name.ShouldBe(name);
        body.Description.ShouldBe("detail");
    }

    [Fact]
    public async Task GET_channels_id_private_hidden_from_non_member()
    {
        // Private channels return 404 (not 403) to non-members so an attacker
        // can't enumerate GUIDs by distinguishing "does not exist" from
        // "exists but hidden". See ChannelsEndpoints.GetChannelDetails.
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);

        var name = $"pd-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "private"), ct);
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var response = await outsider.GetAsync($"/api/channels/{channel.Id:D}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PATCH_channel_renames_and_updates_description()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"pre-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "old", "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var newName = $"post-{Guid.NewGuid():N}"[..20];
        var patch = await client.PatchAsJsonAsync($"/api/channels/{channel.Id:D}",
            new UpdateChannelRequest(newName, "new"), ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        updated.ShouldNotBeNull();
        updated!.Name.ShouldBe(newName);
        updated.Description.ShouldBe("new");
    }

    [Fact]
    public async Task PATCH_channel_forbidden_to_non_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);
        var name = $"fb-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var patch = await outsider.PatchAsJsonAsync($"/api/channels/{channel.Id:D}",
            new UpdateChannelRequest("new-name", null), ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DELETE_channel_soft_deletes_and_hides_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"del-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var del = await client.DeleteAsync($"/api/channels/{channel.Id:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await client.GetAsync($"/api/channels/{channel.Id:D}", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_channel_forbidden_to_non_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);
        var name = $"delFb-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var del = await outsider.DeleteAsync($"/api/channels/{channel.Id:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    internal async Task<(HttpClient Client, string Username)> RegisterFresh(CancellationToken ct)
    {
        var (client, username, _) = await TestHelpers.RegisterFresh(fx, ct);
        return (client, username);
    }
}
