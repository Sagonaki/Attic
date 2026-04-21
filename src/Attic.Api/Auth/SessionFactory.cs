using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Services;

namespace Attic.Api.Auth;

public sealed class SessionFactory(IClock clock)
{
    public (Session Session, string CookieValue) Create(Guid userId, string userAgent, string ip)
    {
        var sessionId = Guid.NewGuid();
        var (tokenPart, tokenHash) = SessionToken.Generate();
        var session = Session.Create(sessionId, userId, tokenHash, userAgent, ip, clock.UtcNow);
        var cookieValue = SessionToken.FormatCookie(sessionId, tokenPart);
        return (session, cookieValue);
    }
}
