namespace Attic.Contracts.Messages;

public sealed record MessageDto(
    long Id,
    Guid ChannelId,
    Guid SenderId,
    string SenderUsername,
    string Content,
    long? ReplyToId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
