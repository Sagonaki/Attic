using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class BlocksFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Search_matches_username_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        var (me, _, _) = await Register(ct);
        var (_, targetUsername, _) = await Register(ct);

        var resp = await me.GetAsync($"/api/users/search?q={targetUsername[..4]}", ct);
        resp.EnsureSuccessStatusCode();
        var rows = (await resp.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!;
        rows.ShouldContain(r => r.Username == targetUsername);
    }

    [Fact]
    public async Task Block_removes_friendship_and_cancels_pending_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);

        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var aliceFriends = await alice.GetAsync("/api/friends", ct);
        var bobId = (await aliceFriends.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!
            .First(f => f.Username == bobUsername).UserId;

        var block = await alice.PostAsync($"/api/users/{bobId:D}/block", null, ct);
        block.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await alice.GetAsync("/api/friends", ct);
        var afterList = (await after.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        afterList.ShouldNotContain(f => f.UserId == bobId);

        var retry = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        retry.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unblock_reenables_friend_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);

        var search = await alice.GetAsync($"/api/users/search?q={bobUsername[..4]}", ct);
        var bobId = (await search.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!
            .First(u => u.Username == bobUsername).Id;

        (await alice.PostAsync($"/api/users/{bobId:D}/block", null, ct)).EnsureSuccessStatusCode();
        (await alice.DeleteAsync($"/api/users/{bobId:D}/block", ct)).EnsureSuccessStatusCode();

        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        send.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
