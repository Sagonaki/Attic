using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class PresenceEventBroadcaster(IHubContext<ChatHub> hub)
{
    /// <summary>Fan-out is the caller's responsibility — this just posts to the listed user groups.</summary>
    public Task PresenceChanged(IReadOnlyList<Guid> recipients, Guid userId, string state)
    {
        if (recipients.Count == 0) return Task.CompletedTask;
        var groups = recipients.Select(id => GroupNames.User(id)).ToArray();
        return hub.Clients.Groups(groups).SendAsync("PresenceChanged", userId, state);
    }
}
