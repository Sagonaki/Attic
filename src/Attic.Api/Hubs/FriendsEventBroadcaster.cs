using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class FriendsEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task FriendRequestReceived(Guid recipientId, FriendRequestDto dto) =>
        hub.Clients.Group(GroupNames.User(recipientId)).SendAsync("FriendRequestReceived", dto);

    public Task FriendRequestDecided(Guid senderId, Guid requestId, string status) =>
        hub.Clients.Group(GroupNames.User(senderId)).SendAsync("FriendRequestDecided", requestId, status);

    public Task FriendRemoved(Guid notifyUserId, Guid otherUserId) =>
        hub.Clients.Group(GroupNames.User(notifyUserId)).SendAsync("FriendRemoved", otherUserId);

    public Task Blocked(Guid blockedUserId, Guid blockerId) =>
        hub.Clients.Group(GroupNames.User(blockedUserId)).SendAsync("Blocked", blockerId);
}
