using System.Buffers;
using Attic.Infrastructure.UnreadCounts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;

namespace Attic.Api.Hubs;

// Background drain: broadcasts MessageCreated to the channel group, then increments
// unread counters and broadcasts UnreadChanged per non-sender member. Runs on a single
// reader loop but fans out the per-member awaits in parallel so the queue drains fast.
public sealed class MessageFanoutService(
    MessageFanoutQueue queue,
    IHubContext<ChatHub> hub,
    IUnreadCountStore unreadCounts,
    ObjectPool<MessageFanoutWorkItem> fanoutPool,
    ILogger<MessageFanoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var memberIdsArray = item.MemberIds;  // Captured before we Return the item so we can release the array.
            var memberCount = item.MemberCount;

            try
            {
                await hub.Clients.Group(GroupNames.Channel(item.ChannelId))
                    .SendAsync("MessageCreated", item.Message, stoppingToken);

                if (memberCount > 0)
                {
                    var channelId = item.ChannelId;
                    var segment = new ArraySegment<Guid>(memberIdsArray, 0, memberCount);
                    var options = new ParallelOptions
                    {
                        CancellationToken = stoppingToken,
                        MaxDegreeOfParallelism = 32,
                    };
                    await Parallel.ForEachAsync(segment, options, async (memberId, ct) =>
                    {
                        var newCount = await unreadCounts.IncrementAsync(memberId, channelId, ct);
                        await hub.Clients.Group(GroupNames.User(memberId))
                            .SendAsync("UnreadChanged", channelId, (int)newCount, ct);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;  // NOTE: the fall-through below still returns the rented array + pooled item via finally.
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Fan-out failed for channel {ChannelId} message {MessageId}",
                    item.ChannelId, item.Message.Id);
            }
            finally
            {
                // Array came from ArrayPool in ChatHub. Return it first — Reset() clears MemberIds reference.
                if (memberIdsArray.Length > 0)
                    ArrayPool<Guid>.Shared.Return(memberIdsArray, clearArray: false);
                fanoutPool.Return(item);
            }
        }
    }
}
