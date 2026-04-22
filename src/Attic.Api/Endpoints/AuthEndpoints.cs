using Attic.Api.Auth;
using Attic.Contracts.Auth;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Audit;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous()
             .RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/delete-account", DeleteAccount).RequireAuthorization();

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

    private static async Task<IResult> DeleteAccount(
        [FromBody] DeleteAccountRequest req,
        IValidator<DeleteAccountRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        CurrentUser currentUser,
        HttpContext http,
        AuditLogContext audit,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var userId = currentUser.UserIdOrThrow;
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        if (!hasher.Verify(user.PasswordHash, req.Password))
            return Results.BadRequest(new ApiError("invalid_password", "Password verification failed."));

        var now = clock.UtcNow;

        // NpgsqlRetryingExecutionStrategy requires wrapping manual transactions with CreateExecutionStrategy.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Owned channels → hard delete dependents explicitly, then the channels themselves.
            var ownedChannelIds = await db.Channels.AsNoTracking()
                .Where(c => c.OwnerId == userId)
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (ownedChannelIds.Count > 0)
            {
                await db.ChannelInvitations.Where(i => ownedChannelIds.Contains(i.ChannelId)).ExecuteDeleteAsync(ct);
                await db.ChannelMembers.IgnoreQueryFilters().Where(m => ownedChannelIds.Contains(m.ChannelId)).ExecuteDeleteAsync(ct);
                await db.ChannelReads.Where(r => ownedChannelIds.Contains(r.ChannelId)).ExecuteDeleteAsync(ct);
                // Delete attachments in owned channel messages (two-step to avoid subquery translation issues).
                var ownedMsgIds = await db.Messages.IgnoreQueryFilters()
                    .Where(m => ownedChannelIds.Contains(m.ChannelId))
                    .Select(m => m.Id)
                    .ToListAsync(ct);
                if (ownedMsgIds.Count > 0)
                    await db.Attachments.Where(a => a.MessageId != null && ownedMsgIds.Contains(a.MessageId!.Value)).ExecuteDeleteAsync(ct);
                await db.Messages.IgnoreQueryFilters().Where(m => ownedChannelIds.Contains(m.ChannelId)).ExecuteDeleteAsync(ct);
                await db.Channels.Where(c => ownedChannelIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
            }

            // Delete non-owned memberships and reads.
            await db.ChannelMembers.IgnoreQueryFilters().Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
            await db.ChannelReads.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);

            // Friend graph.
            await db.Friendships.Where(f => f.UserAId == userId || f.UserBId == userId).ExecuteDeleteAsync(ct);
            await db.FriendRequests.Where(r => r.SenderId == userId || r.RecipientId == userId).ExecuteDeleteAsync(ct);
            await db.UserBlocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ExecuteDeleteAsync(ct);

            // Sessions.
            await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);

            // Invitations sent by or targeting this user (cross-channel).
            await db.ChannelInvitations.Where(i => i.InviterId == userId || i.InviteeId == userId).ExecuteDeleteAsync(ct);

            // Soft-delete the user with tombstone rewrite.
            user.SoftDelete(now);
            audit.Add(
                action: "account.delete",
                actorUserId: userId);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        // Clear the caller's cookie.
        http.Response.Cookies.Delete(AtticAuthenticationOptions.CookieName);
        return Results.NoContent();
    }
}
