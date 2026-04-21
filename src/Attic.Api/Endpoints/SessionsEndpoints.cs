using Attic.Api.Auth;
using Attic.Api.Hubs;
using Attic.Contracts.Sessions;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/sessions").RequireAuthorization();

        group.MapGet("/", ListMine);
        group.MapDelete("/{id:guid}", Revoke);

        return routes;
    }

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;
        var currentSessionId = currentUser.SessionIdOrNull;
        var now = clock.UtcNow;

        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new ActiveSessionDto(
                s.Id, s.UserAgent ?? "", s.Ip, s.CreatedAt, s.LastSeenAt,
                IsCurrent: s.Id == currentSessionId))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Revoke(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        SessionsEventBroadcaster events,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var session = await db.Sessions.AsTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return Results.NotFound();
        if (session.RevokedAt is not null) return Results.NoContent();

        session.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await events.ForceLogout(id);
        return Results.NoContent();
    }
}
