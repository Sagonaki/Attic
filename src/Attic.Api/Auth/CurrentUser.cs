using System.Security.Claims;

namespace Attic.Api.Auth;

public sealed class CurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }

    public bool IsAuthenticated => UserId.HasValue;
    public Guid UserIdOrThrow => UserId ?? throw new InvalidOperationException("Not authenticated.");
    public Guid SessionIdOrThrow => SessionId ?? throw new InvalidOperationException("Not authenticated.");

    public static Guid? ReadUserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst(AtticClaims.UserId)?.Value, out var id) ? id : null;

    public static Guid? ReadSessionId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst(AtticClaims.SessionId)?.Value, out var id) ? id : null;
}

public static class AtticClaims
{
    public const string UserId = "attic:uid";
    public const string SessionId = "attic:sid";
}
