using Attic.Api.Auth;
using Attic.Contracts.Auth;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> Register(
        RegisterRequest req,
        IValidator<RegisterRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        SessionFactory sessionFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid) return Results.BadRequest(new ApiError("validation_failed", vr.ToString()));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var trimmedUsername = req.Username.Trim();

        var emailTaken = await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (emailTaken) return Results.Conflict(new ApiError("email_taken", "Email is already registered."));

        var usernameTaken = await db.Users.AnyAsync(u => u.Username == trimmedUsername, ct);
        if (usernameTaken) return Results.Conflict(new ApiError("username_taken", "Username is already taken."));

        var user = User.Register(Guid.NewGuid(), normalizedEmail, trimmedUsername, hasher.Hash(req.Password), clock.UtcNow);
        db.Users.Add(user);

        var userAgent = http.Request.Headers.UserAgent.ToString();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
        var (session, cookieValue) = sessionFactory.Create(user.Id, userAgent, ip);
        db.Sessions.Add(session);

        await db.SaveChangesAsync(ct);

        http.Response.Cookies.Append(AtticAuthenticationOptions.CookieName, cookieValue, AuthExtensions.CreateSessionCookieOptions(http.Request, session.ExpiresAt));
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }

    private static async Task<IResult> Login(
        LoginRequest req,
        IValidator<LoginRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        SessionFactory sessionFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid) return Results.BadRequest(new ApiError("validation_failed", vr.ToString()));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is null || !hasher.Verify(user.PasswordHash, req.Password))
            return Results.Unauthorized();

        var userAgent = http.Request.Headers.UserAgent.ToString();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
        var (session, cookieValue) = sessionFactory.Create(user.Id, userAgent, ip);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        http.Response.Cookies.Append(AtticAuthenticationOptions.CookieName, cookieValue, AuthExtensions.CreateSessionCookieOptions(http.Request, session.ExpiresAt));
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }

    private static async Task<IResult> Logout(
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        HttpContext http,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var session = await db.Sessions.AsTracking().FirstOrDefaultAsync(s => s.Id == currentUser.SessionIdOrThrow, ct);
        if (session is not null)
        {
            session.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        http.Response.Cookies.Delete(AtticAuthenticationOptions.CookieName);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(AtticDbContext db, CurrentUser currentUser, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserIdOrThrow, ct);
        if (user is null) return Results.Unauthorized();
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }
}
