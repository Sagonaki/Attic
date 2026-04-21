using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class DeleteAccountFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Delete_account_cascades_owned_channels_and_friendships()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, aliceEmail, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await alice.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"del-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (bob, bobUsername, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var fr = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{fr.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var del = await alice.PostAsJsonAsync("/api/auth/delete-account",
            new DeleteAccountRequest("hunter2pw"), ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Alice's channel is gone.
        var view = await bob.GetAsync($"/api/channels/{channel.Id:D}", ct);
        view.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Bob has no friends now.
        var bobFriends = (await (await bob.GetAsync("/api/friends", ct))
            .Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        bobFriends.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_rejects_wrong_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var del = await client.PostAsJsonAsync("/api/auth/delete-account",
            new DeleteAccountRequest("wrong-pw"), ct);
        del.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
