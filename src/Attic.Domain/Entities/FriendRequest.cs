using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class FriendRequest
{
    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid RecipientId { get; private set; }
    public string? Text { get; private set; }
    public FriendRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private FriendRequest() { }

    public static FriendRequest Issue(Guid id, Guid senderId, Guid recipientId, string? text, DateTimeOffset now)
    {
        if (senderId == recipientId)
            throw new ArgumentException("Cannot send a friend request to yourself.", nameof(recipientId));

        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmed is not null && trimmed.Length > 500)
            throw new ArgumentException("Text is limited to 500 characters.", nameof(text));

        return new FriendRequest
        {
            Id = id,
            SenderId = senderId,
            RecipientId = recipientId,
            Text = trimmed,
            Status = FriendRequestStatus.Pending,
            CreatedAt = now
        };
    }

    public void Accept(DateTimeOffset at) => Decide(FriendRequestStatus.Accepted, at);
    public void Decline(DateTimeOffset at) => Decide(FriendRequestStatus.Declined, at);
    public void Cancel(DateTimeOffset at) => Decide(FriendRequestStatus.Cancelled, at);

    private void Decide(FriendRequestStatus next, DateTimeOffset at)
    {
        if (Status != FriendRequestStatus.Pending)
            throw new InvalidOperationException($"Friend request is already {Status}.");
        Status = next;
        DecidedAt = at;
    }
}
