namespace Attic.Domain.Entities;

public sealed class Friendship
{
    public Guid UserAId { get; private set; }
    public Guid UserBId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Friendship() { }

    public static Friendship Create(Guid userAId, Guid userBId, DateTimeOffset now)
    {
        if (userAId == userBId)
            throw new ArgumentException("Friendship requires two distinct users.", nameof(userBId));

        var (smaller, larger) = userAId.CompareTo(userBId) < 0 ? (userAId, userBId) : (userBId, userAId);
        return new Friendship { UserAId = smaller, UserBId = larger, CreatedAt = now };
    }

    public bool Involves(Guid userId) => userId == UserAId || userId == UserBId;

    public Guid Other(Guid userId)
    {
        if (userId == UserAId) return UserBId;
        if (userId == UserBId) return UserAId;
        throw new ArgumentException("User is not part of this friendship.", nameof(userId));
    }
}
