namespace Attic.Contracts.Channels;

public sealed record ChannelMemberSummary(
    Guid UserId,
    string Username,
    string Role,           // "owner" | "admin" | "member"
    DateTimeOffset JoinedAt);
