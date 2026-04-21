using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class ChannelInvitation
{
    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid InviterId { get; private set; }
    public Guid InviteeId { get; private set; }
    public InvitationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private ChannelInvitation() { }

    public static ChannelInvitation Issue(Guid id, Guid channelId, Guid inviterId, Guid inviteeId, DateTimeOffset now)
    {
        if (inviterId == inviteeId)
            throw new ArgumentException("Cannot invite yourself.", nameof(inviteeId));

        return new ChannelInvitation
        {
            Id = id,
            ChannelId = channelId,
            InviterId = inviterId,
            InviteeId = inviteeId,
            Status = InvitationStatus.Pending,
            CreatedAt = now
        };
    }

    public void Accept(DateTimeOffset at) => Decide(InvitationStatus.Accepted, at);
    public void Decline(DateTimeOffset at) => Decide(InvitationStatus.Declined, at);
    public void Cancel(DateTimeOffset at) => Decide(InvitationStatus.Cancelled, at);

    private void Decide(InvitationStatus next, DateTimeOffset at)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Invitation is already {Status}.");
        Status = next;
        DecidedAt = at;
    }
}
