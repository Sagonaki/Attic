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
public sealed class MembershipFlowTests(AppHostFixture fx)
{
    private async Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => await TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Join_public_channel_succeeds_and_shows_up_in_mine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var name = $"pub-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        var join = await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", content: null, ct);
        join.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await joiner.GetAsync("/api/channels/mine", ct);
        mine.EnsureSuccessStatusCode();
        var body = await mine.Content.ReadFromJsonAsync<List<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.ShouldContain(c => c.GetProperty("id").GetGuid() == channel.Id);
    }

    [Fact]
    public async Task Join_private_channel_rejected_without_invitation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"pr-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _, _) = await Register(ct);
        var join = await outsider.PostAsync($"/api/channels/{channel.Id:D}/join", content: null, ct);
        join.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await join.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("CannotAutoJoinPrivate");
    }
}
