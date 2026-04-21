using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class FriendsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Send_and_accept_friend_request_creates_friendship()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, "hi"), ct);
        send.EnsureSuccessStatusCode();
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        dto.Status.ShouldBe("pending");
        dto.Text.ShouldBe("hi");

        var inbox = await recipient.GetAsync("/api/friend-requests", ct);
        var pending = (await inbox.Content.ReadFromJsonAsync<List<FriendRequestDto>>(ct))!;
        pending.ShouldContain(r => r.Id == dto.Id);

        var accept = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Decline_marks_declined_and_no_friendship_created()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;

        var decline = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/decline", null, ct);
        decline.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var repeat = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct);
        repeat.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await repeat.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("not_pending");
    }

    [Fact]
    public async Task Cannot_send_duplicate_pending_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (_, recipientUsername, _) = await Register(ct);

        (await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct)).EnsureSuccessStatusCode();

        var second = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await second.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("DuplicateFriendRequest");
    }

    [Fact]
    public async Task Cannot_send_to_self()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, username, _) = await Register(ct);

        var send = await client.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(username, null), ct);
        send.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await send.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("self_request");
    }
}
