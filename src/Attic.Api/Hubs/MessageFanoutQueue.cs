using System.Threading.Channels;
using Attic.Contracts.Messages;

namespace Attic.Api.Hubs;

public sealed record MessageFanoutWorkItem(
    Guid ChannelId,
    MessageDto Message,
    IReadOnlyList<Guid> MemberIds);

public interface IMessageFanoutQueue
{
    bool TryEnqueue(MessageFanoutWorkItem item);
}

// Unbounded single-reader queue. Hub producers hand off work and return; the background
// service drains FIFO so per-channel order is preserved by whoever-wins-at-the-hub ordering.
public sealed class MessageFanoutQueue : IMessageFanoutQueue
{
    private readonly Channel<MessageFanoutWorkItem> _channel =
        Channel.CreateUnbounded<MessageFanoutWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public ChannelReader<MessageFanoutWorkItem> Reader => _channel.Reader;

    public bool TryEnqueue(MessageFanoutWorkItem item) => _channel.Writer.TryWrite(item);
}
