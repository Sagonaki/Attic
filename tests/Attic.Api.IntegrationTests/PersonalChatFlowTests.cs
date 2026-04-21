using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class PersonalChatFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    private static async Task MakeFriends(HttpClient a, HttpClient b, string bUsername, CancellationToken ct)
    {
        var send = await a.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await b.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Open_personal_chat_creates_channel_with_two_members()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);
        await MakeFriends(alice, bob, bobUsername, ct);

        var open = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        open.StatusCode.ShouldBe(HttpStatusCode.OK);
        var channel = (await open.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        channel.Kind.ShouldBe("personal");
        channel.Name.ShouldBeNull();
        channel.OwnerId.ShouldBeNull();
        channel.MemberCount.ShouldBe(2);

        var aliceMine = (await (await alice.GetAsync("/api/channels/mine", ct))
            .Content.ReadFromJsonAsync<List<ChannelSummary>>(ct))!;
        aliceMine.ShouldContain(c => c.Id == channel.Id && c.Kind == "personal");

        var bobMine = (await (await bob.GetAsync("/api/channels/mine", ct))
            .Content.ReadFromJsonAsync<List<ChannelSummary>>(ct))!;
        bobMine.ShouldContain(c => c.Id == channel.Id && c.Kind == "personal");
    }

    [Fact]
    public async Task Open_personal_chat_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);
        await MakeFriends(alice, bob, bobUsername, ct);

        var first = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        var c1 = (await first.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var second = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        var c2 = (await second.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        c1.Id.ShouldBe(c2.Id);
    }

    [Fact]
    public async Task Open_rejected_when_not_friends()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (_, bobUsername, _) = await Register(ct);

        var open = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        open.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await open.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("NotFriends");
    }
}
