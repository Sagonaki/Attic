using System.Security.Cryptography;
using System.Text;

namespace Attic.Domain.Services;

public static class SessionToken
{
    public static (string TokenPart, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var tokenPart = Base64Url(bytes);
        var hash = ComputeHash(tokenPart);
        return (tokenPart, hash);
    }

    public static string ComputeHash(string tokenPart)
    {
        var bytes = Encoding.UTF8.GetBytes(tokenPart ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static bool Verify(string storedHash, string presentedTokenPart)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(presentedTokenPart)) return false;
        var presented = ComputeHash(presentedTokenPart);
        var a = Encoding.ASCII.GetBytes(storedHash);
        var b = Encoding.ASCII.GetBytes(presented);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string FormatCookie(Guid sessionId, string tokenPart)
        => $"{sessionId:N}.{tokenPart}";

    public static (Guid SessionId, string TokenPart)? ParseCookie(string? cookieValue)
    {
        if (string.IsNullOrEmpty(cookieValue)) return null;
        var dot = cookieValue.IndexOf('.');
        if (dot <= 0 || dot == cookieValue.Length - 1) return null;
        var left = cookieValue[..dot];
        var right = cookieValue[(dot + 1)..];
        if (!Guid.TryParseExact(left, "N", out var sid)) return null;
        return (sid, right);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
