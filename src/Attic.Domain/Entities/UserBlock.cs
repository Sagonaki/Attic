namespace Attic.Domain.Entities;

public sealed class UserBlock
{
    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private UserBlock() { }

    public static UserBlock Create(Guid blockerId, Guid blockedId, DateTimeOffset now)
    {
        if (blockerId == blockedId)
            throw new ArgumentException("Cannot block yourself.", nameof(blockedId));

        return new UserBlock { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = now };
    }
}
