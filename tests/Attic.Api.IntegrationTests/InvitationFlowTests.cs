using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Invitations;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class InvitationFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Invite_accept_adds_user_to_private_room()
    {
        var ct = TestContext.Current.CancellationToken;

        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"vip-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (invitee, inviteeUsername, _) = await Register(ct);

        var invite = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        invite.EnsureSuccessStatusCode();

        var inbox = await invitee.GetAsync("/api/invitations", ct);
        inbox.EnsureSuccessStatusCode();
        var pending = (await inbox.Content.ReadFromJsonAsync<List<InvitationDto>>(ct))!;
        pending.ShouldNotBeEmpty();
        var inv = pending.First(p => p.ChannelId == channel.Id);

        var accept = await invitee.PostAsync($"/api/invitations/{inv.Id:D}/accept", null, ct);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await invitee.GetAsync("/api/channels/mine", ct);
        var body = await mine.Content.ReadFromJsonAsync<List<ChannelSummary>>(ct);
        body!.ShouldContain(c => c.Id == channel.Id);
    }

    [Fact]
    public async Task Decline_invitation_does_not_add_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"dec-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (invitee, inviteeUsername, _) = await Register(ct);
        (await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct)).EnsureSuccessStatusCode();

        var inbox = await invitee.GetAsync("/api/invitations", ct);
        var pending = (await inbox.Content.ReadFromJsonAsync<List<InvitationDto>>(ct))!;
        var inv = pending.First();

        var decline = await invitee.PostAsync($"/api/invitations/{inv.Id:D}/decline", null, ct);
        decline.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await invitee.GetAsync("/api/channels/mine", ct);
        var body = await mine.Content.ReadFromJsonAsync<List<ChannelSummary>>(ct);
        body!.ShouldNotContain(c => c.Id == channel.Id);
    }

    [Fact]
    public async Task Cannot_invite_to_public_room()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"pub-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (_, inviteeUsername, _) = await Register(ct);
        var invite = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        invite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await invite.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("CannotInviteToPublic");
    }

    [Fact]
    public async Task Duplicate_pending_invitation_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"dup-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (_, inviteeUsername, _) = await Register(ct);

        (await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct)).EnsureSuccessStatusCode();

        var second = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await second.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("AlreadyInvited");
    }
}
