namespace Attic.Contracts.Messages;

public sealed record SendMessageRequest(
    Guid ChannelId,
    Guid ClientMessageId,
    string Content,
    long? ReplyToId,
    Guid[]? AttachmentIds = null);
