using Attic.Contracts.Channels;
using Attic.Contracts.Invitations;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class ChannelEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task ChannelMemberJoined(Guid channelId, ChannelMemberSummary summary) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberJoined", channelId, summary);

    public Task ChannelMemberLeft(Guid channelId, Guid userId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberLeft", channelId, userId);

    public Task ChannelMemberRoleChanged(Guid channelId, Guid userId, string role) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberRoleChanged", channelId, userId, role);

    public Task ChannelDeleted(Guid channelId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelDeleted", channelId);

    public Task RemovedFromChannel(Guid userId, Guid channelId, string reason) =>
        hub.Clients.Group(GroupNames.User(userId)).SendAsync("RemovedFromChannel", channelId, reason);

    public Task InvitationReceived(Guid userId, InvitationDto invitation) =>
        hub.Clients.Group(GroupNames.User(userId)).SendAsync("InvitationReceived", invitation);

    public Task MessageDeleted(Guid channelId, long messageId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("MessageDeleted", channelId, messageId);
}
