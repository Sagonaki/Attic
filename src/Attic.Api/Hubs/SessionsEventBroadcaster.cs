using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class SessionsEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task ForceLogout(Guid sessionId) =>
        hub.Clients.Group(GroupNames.Session(sessionId)).SendAsync("ForceLogout", sessionId);
}
