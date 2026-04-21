namespace Attic.Domain.Entities;

public sealed class Session
{
    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromDays(30);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string UserAgent { get; private set; } = default!;
    public string Ip { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Session() { }

    public static Session Create(Guid id, Guid userId, string tokenHash, string userAgent, string ip, DateTimeOffset now)
    {
        return new Session
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash ?? throw new ArgumentNullException(nameof(tokenHash)),
            UserAgent = userAgent ?? string.Empty,
            Ip = ip ?? string.Empty,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + SlidingLifetime
        };
    }

    public bool IsValidAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Touch(DateTimeOffset now)
    {
        LastSeenAt = now;
        ExpiresAt = now + SlidingLifetime;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
