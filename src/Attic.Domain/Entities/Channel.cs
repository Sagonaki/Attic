using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class Channel
{
    public Guid Id { get; private set; }
    public ChannelKind Kind { get; private set; }
    public string? Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? OwnerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private Channel() { }

    public static Channel CreateRoom(Guid id, ChannelKind kind, string name, string? description, Guid ownerId, DateTimeOffset now)
    {
        if (kind == ChannelKind.Personal)
            throw new ArgumentException("Use CreatePersonal for personal channels.", nameof(kind));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required for room channels.", nameof(name));

        return new Channel
        {
            Id = id,
            Kind = kind,
            Name = name.Trim(),
            Description = description?.Trim(),
            OwnerId = ownerId,
            CreatedAt = now
        };
    }

    public static Channel CreatePersonal(Guid id, DateTimeOffset now)
    {
        return new Channel
        {
            Id = id,
            Kind = ChannelKind.Personal,
            Name = null,
            Description = null,
            OwnerId = null,
            CreatedAt = now
        };
    }

    public void SoftDelete(DateTimeOffset at) => DeletedAt ??= at;
}
