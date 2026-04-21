using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Attic.Infrastructure.Auth;

public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly User Dummy = CreateDummy();
    private readonly Microsoft.AspNetCore.Identity.IPasswordHasher<User> _inner;

    public PasswordHasherAdapter(Microsoft.AspNetCore.Identity.IPasswordHasher<User> inner) => _inner = inner;

    public string Hash(string password) => _inner.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
    {
        var result = _inner.VerifyHashedPassword(Dummy, hash, password);
        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static User CreateDummy() =>
        User.Register(Guid.Empty, "dummy@void.local", "dummy", "placeholder", DateTimeOffset.UnixEpoch);
}
