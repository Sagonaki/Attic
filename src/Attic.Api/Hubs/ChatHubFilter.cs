using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class ChatHubFilter(ILogger<ChatHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (HubException)
        {
            throw;  // already meant for the client
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {Method} failed for user {User}", context.HubMethodName, context.Context.UserIdentifier);
            throw new HubException("Something went wrong. Please try again.");
        }
    }
}
