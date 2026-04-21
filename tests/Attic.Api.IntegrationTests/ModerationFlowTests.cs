using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Channels;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class ModerationFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Admin_bans_member_and_member_cannot_post_or_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ban-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var joinerMembers = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var memberList = await joinerMembers.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerUserId = memberList!.First(m => m.Role == "member").UserId;

        var ban = await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerUserId:D}", ct);
        ban.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rejoin = await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct);
        rejoin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_admin_cannot_ban()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"na-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (m1, _, _) = await Register(ct);
        (await m1.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var (m2, _, _) = await Register(ct);
        (await m2.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var anyMember = members!.First(m => m.Role == "member");

        var ban = await m1.DeleteAsync($"/api/channels/{channel.Id:D}/members/{anyMember.UserId:D}", ct);
        ban.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_can_promote_member_to_admin_and_demote_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rl-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Role == "member").UserId;

        var promote = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/members/{joinerId:D}/role",
            new ChangeRoleRequest("admin"), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterPromote = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var afterPromoteBody = await afterPromote.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        afterPromoteBody!.First(m => m.UserId == joinerId).Role.ShouldBe("admin");

        var demote = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/members/{joinerId:D}/role",
            new ChangeRoleRequest("member"), ct);
        demote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Bans_list_shows_banned_users_and_unban_allows_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ub-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, joinerName, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Username == joinerName).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        var bans = await owner.GetAsync($"/api/channels/{channel.Id:D}/bans", ct);
        bans.EnsureSuccessStatusCode();
        var bansBody = await bans.Content.ReadFromJsonAsync<List<BannedMemberSummary>>(ct);
        bansBody.ShouldNotBeNull();
        bansBody!.ShouldContain(b => b.UserId == joinerId);

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/bans/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        var afterList = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var afterBody = await afterList.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        afterBody!.ShouldContain(m => m.UserId == joinerId && m.Role == "member");
    }
}
