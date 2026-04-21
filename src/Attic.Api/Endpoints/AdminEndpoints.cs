using Attic.Api.Auth;
using Attic.Contracts.Admin;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin").RequireAuthorization();
        group.MapGet("/audit/mine", GetMyAudit);
        return routes;
    }

    private static async Task<IResult> GetMyAudit(
        int? limit,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(l => l.ActorUserId == me)
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Select(l => new AuditLogEntryDto(
                l.Id, l.ActorUserId, l.Action, l.TargetChannelId, l.TargetUserId, l.TargetMessageId, l.Data, l.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }
}
