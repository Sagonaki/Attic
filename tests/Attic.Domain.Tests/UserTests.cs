using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class UserTests
{
    [Fact]
    public void Register_creates_user_with_normalized_email_and_trimmed_username()
    {
        var user = User.Register(
            id: Guid.NewGuid(),
            email: "  ALICE@example.COM ",
            username: "  alice ",
            passwordHash: "hash",
            createdAt: DateTimeOffset.UnixEpoch);

        user.Email.ShouldBe("alice@example.com");
        user.Username.ShouldBe("alice");
        user.PasswordHash.ShouldBe("hash");
        user.CreatedAt.ShouldBe(DateTimeOffset.UnixEpoch);
        user.DeletedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@bad.com")]
    public void Register_rejects_invalid_email(string email)
    {
        var act = () => User.Register(Guid.NewGuid(), email, "alice", "hash", DateTimeOffset.UnixEpoch);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("email");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]                       // too short
    [InlineData("this-username-is-way-too-long-123")]   // too long (33)
    [InlineData("has space")]
    [InlineData("has@sign")]
    public void Register_rejects_invalid_username(string username)
    {
        var act = () => User.Register(Guid.NewGuid(), "a@b.co", username, "hash", DateTimeOffset.UnixEpoch);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("username");
    }

    [Fact]
    public void SoftDelete_sets_DeletedAt_and_tombstones_email_and_username()
    {
        var user = User.Register(Guid.Parse("11111111-1111-1111-1111-111111111111"), "a@b.co", "alice", "hash", DateTimeOffset.UnixEpoch);
        user.SoftDelete(DateTimeOffset.UnixEpoch.AddDays(1));

        user.DeletedAt.ShouldBe(DateTimeOffset.UnixEpoch.AddDays(1));
        user.Email.ShouldBe("deleted-11111111-1111-1111-1111-111111111111@void");
        // Username is capped at 32 chars: "deleted-" (8) + first 24 hex chars of GUID (N format).
        user.Username.ShouldBe("deleted-111111111111111111111111");
    }
}
