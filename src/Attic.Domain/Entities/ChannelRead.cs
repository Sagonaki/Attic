namespace Attic.Domain.Entities;

public sealed class ChannelRead
{
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public long LastReadMessageId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChannelRead() { }

    public static ChannelRead Create(Guid channelId, Guid userId, long lastReadMessageId, DateTimeOffset now)
        => new()
        {
            ChannelId = channelId,
            UserId = userId,
            LastReadMessageId = lastReadMessageId,
            UpdatedAt = now
        };

    public void MarkRead(long messageId, DateTimeOffset at)
    {
        if (messageId > LastReadMessageId) LastReadMessageId = messageId;
        UpdatedAt = at;
    }
}
