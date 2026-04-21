using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class ChannelMember
{
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public ChannelRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? BannedAt { get; private set; }
    public Guid? BannedById { get; private set; }
    public string? BanReason { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private ChannelMember() { }

    public static ChannelMember Join(Guid channelId, Guid userId, ChannelRole role, DateTimeOffset now)
    {
        return new ChannelMember
        {
            ChannelId = channelId,
            UserId = userId,
            Role = role,
            JoinedAt = now
        };
    }

    public void Ban(Guid bannedBy, string? reason, DateTimeOffset at)
    {
        BannedAt = at;
        BannedById = bannedBy;
        BanReason = reason;
    }

    public void Unban()
    {
        BannedAt = null;
        BannedById = null;
        BanReason = null;
        if (Role == ChannelRole.Owner) return;  // owner role never auto-restored; unban returns to Member
        Role = ChannelRole.Member;
    }

    public void ChangeRole(ChannelRole newRole)
    {
        if (Role == ChannelRole.Owner) throw new InvalidOperationException("Owner role cannot be changed.");
        Role = newRole;
    }
}
