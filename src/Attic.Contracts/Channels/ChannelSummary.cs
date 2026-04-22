namespace Attic.Contracts.Channels;

public sealed record ChannelSummary(
    Guid Id,
    string Kind,      // "public" | "private" | "personal"
    string? Name,
    string? Description,
    Guid? OwnerId,
    int MemberCount,
    int UnreadCount,
    string? OtherMemberUsername = null);
