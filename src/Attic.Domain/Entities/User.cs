using System.Text.RegularExpressions;

namespace Attic.Domain.Entities;

public sealed class User
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[A-Za-z0-9_\-]{3,32}$", RegexOptions.Compiled);

    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private User() { }

    public static User Register(Guid id, string email, string username, string passwordHash, DateTimeOffset createdAt)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (!EmailRegex.IsMatch(normalizedEmail))
            throw new ArgumentException("Invalid email.", nameof(email));

        var trimmedUsername = (username ?? string.Empty).Trim();
        if (!UsernameRegex.IsMatch(trimmedUsername))
            throw new ArgumentException("Username must be 3-32 chars of [A-Za-z0-9_-].", nameof(username));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new User
        {
            Id = id,
            Email = normalizedEmail,
            Username = trimmedUsername,
            PasswordHash = passwordHash,
            CreatedAt = createdAt
        };
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        var idHex = Id.ToString("N");             // 32 hex chars, no dashes
        Email = $"deleted-{Id:D}@void";           // email column is 320 max
        Username = $"deleted-{idHex}"[..32];      // username column is 32 max; "deleted-" = 8, take 24 hex chars
    }

    public void ChangePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new ArgumentException("Password hash is required.", nameof(newHash));
        PasswordHash = newHash;
    }
}
