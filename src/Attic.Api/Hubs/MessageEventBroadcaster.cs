using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class MessageEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task MessageEdited(Guid channelId, long messageId, string newContent, DateTimeOffset updatedAt) =>
        hub.Clients.Group(GroupNames.Channel(channelId))
            .SendAsync("MessageEdited", channelId, messageId, newContent, updatedAt);
}
