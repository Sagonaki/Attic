using Attic.Infrastructure.UnreadCounts;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

// Background drain: broadcasts MessageCreated to the channel group, then increments
// unread counters and broadcasts UnreadChanged per non-sender member. Runs on a single
// reader loop but fans out the per-member awaits in parallel so the queue drains fast.
public sealed class MessageFanoutService(
    MessageFanoutQueue queue,
    IHubContext<ChatHub> hub,
    IUnreadCountStore unreadCounts,
    ILogger<MessageFanoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await hub.Clients.Group(GroupNames.Channel(item.ChannelId))
                    .SendAsync("MessageCreated", item.Message, stoppingToken);

                var unreadTasks = item.MemberIds.Select(async memberId =>
                {
                    var newCount = await unreadCounts.IncrementAsync(memberId, item.ChannelId, stoppingToken);
                    await hub.Clients.Group(GroupNames.User(memberId))
                        .SendAsync("UnreadChanged", item.ChannelId, (int)newCount, stoppingToken);
                });
                await Task.WhenAll(unreadTasks);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Fan-out failed for channel {ChannelId} message {MessageId}",
                    item.ChannelId, item.Message.Id);
            }
        }
    }
}
