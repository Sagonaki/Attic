using System.Security.Claims;
using System.Text.Encodings.Web;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Attic.Api.Auth;

public sealed class AtticAuthenticationHandler(
    IOptionsMonitor<AtticAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtticDbContext db,
    IClock clock,
    CurrentUser currentUser) : AuthenticationHandler<AtticAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Context.Request.Cookies[AtticAuthenticationOptions.CookieName];
        var parsed = Attic.Domain.Services.SessionToken.ParseCookie(cookie);
        if (parsed is null) return AuthenticateResult.NoResult();

        var (sessionId, tokenPart) = parsed.Value;
        var session = await db.Sessions.AsTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return AuthenticateResult.Fail("Session not found.");
        if (!session.IsValidAt(clock.UtcNow)) return AuthenticateResult.Fail("Session not valid.");
        if (!Attic.Domain.Services.SessionToken.Verify(session.TokenHash, tokenPart)) return AuthenticateResult.Fail("Session token mismatch.");

        // Throttle LastSeenAt/ExpiresAt updates to at most once per 30s per session.
        if ((clock.UtcNow - session.LastSeenAt) > TimeSpan.FromSeconds(30))
        {
            session.Touch(clock.UtcNow);
            await db.SaveChangesAsync();
        }

        currentUser.UserId = session.UserId;
        currentUser.SessionId = session.Id;

        var identity = new ClaimsIdentity(AtticAuthenticationOptions.Scheme);
        identity.AddClaim(new Claim(AtticClaims.UserId, session.UserId.ToString()));
        identity.AddClaim(new Claim(AtticClaims.SessionId, session.Id.ToString()));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AtticAuthenticationOptions.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
