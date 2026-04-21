using System.Text;

namespace Attic.Domain.Entities;

public sealed class Message
{
    public const int MaxContentBytes = 3072;

    public long Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = default!;
    public long? ReplyToId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Message() { }

    public static Message Post(Guid channelId, Guid senderId, string content, long? replyToId, DateTimeOffset now)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount == 0) throw new ArgumentException("Message content cannot be empty.", nameof(content));
        if (byteCount > MaxContentBytes) throw new ArgumentException($"Message content exceeds {MaxContentBytes} bytes.", nameof(content));

        return new Message
        {
            ChannelId = channelId,
            SenderId = senderId,
            Content = content,
            ReplyToId = replyToId,
            CreatedAt = now
        };
    }

    public void Edit(string newContent, DateTimeOffset at)
    {
        if (DeletedAt is not null) throw new InvalidOperationException("Cannot edit a deleted message.");
        var byteCount = Encoding.UTF8.GetByteCount(newContent);
        if (byteCount == 0 || byteCount > MaxContentBytes)
            throw new ArgumentException($"Content must be 1..{MaxContentBytes} bytes.", nameof(newContent));
        Content = newContent;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at) => DeletedAt ??= at;
}
