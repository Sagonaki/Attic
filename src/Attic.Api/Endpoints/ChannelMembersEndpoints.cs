using Attic.Api.Auth;
using Attic.Api.Hubs;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Attic.Infrastructure.Audit;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class ChannelMembersEndpoints
{
    public static IEndpointRouteBuilder MapChannelMembersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/channels/{channelId:guid}").RequireAuthorization();

        group.MapGet("/members", ListMembers);
        group.MapDelete("/members/{userId:guid}", BanMember);
        group.MapPost("/members/{userId:guid}/role", ChangeMemberRole);
        group.MapGet("/bans", ListBans);
        group.MapDelete("/bans/{userId:guid}", UnbanMember);

        return routes;
    }

    private static async Task<IResult> ListMembers(
        Guid channelId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null) return Results.NotFound();

        var userId = currentUser.UserIdOrThrow;
        var callerIsMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        if (channel.Kind == ChannelKind.Private && !callerIsMember) return Results.Forbid();

        var rows = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.UserId)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.UserId, u => u.Id,
                  (m, u) => new ChannelMemberSummary(
                      u.Id, u.Username, m.Role.ToString().ToLowerInvariant(), m.JoinedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> BanMember(
        Guid channelId,
        Guid userId,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        ChannelEventBroadcaster events,
        AuditLogContext audit,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var target = await db.ChannelMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        var auth = AuthorizationRules.CanBanFromChannel(actor, target);
        if (!auth.Allowed) return Results.Forbid();

        target!.Ban(actorId, reason: null, at: clock.UtcNow);
        audit.Add(
            action: "channel.ban_member",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId);
        await db.SaveChangesAsync(ct);

        await events.ChannelMemberLeft(channelId, userId);
        await events.RemovedFromChannel(userId, channelId, "banned");

        return Results.NoContent();
    }

    private static async Task<IResult> ChangeMemberRole(
        Guid channelId,
        Guid userId,
        [FromBody] ChangeRoleRequest req,
        IValidator<ChangeRoleRequest> validator,
        AtticDbContext db,
        CurrentUser currentUser,
        ChannelEventBroadcaster events,
        AuditLogContext audit,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var newRole = req.Role == "admin" ? ChannelRole.Admin : ChannelRole.Member;

        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var target = await db.ChannelMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        var auth = AuthorizationRules.CanChangeRole(actor, target, newRole);
        if (!auth.Allowed) return Results.Forbid();

        target!.ChangeRole(newRole);
        audit.Add(
            action: "channel.change_role",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId,
            dataJson: $"{{\"newRole\":\"{newRole.ToString().ToLowerInvariant()}\"}}");;
        await db.SaveChangesAsync(ct);

        await events.ChannelMemberRoleChanged(channelId, userId, newRole.ToString().ToLowerInvariant());

        return Results.NoContent();
    }

    private static async Task<IResult> ListBans(
        Guid channelId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var auth = AuthorizationRules.CanManageChannel(actor);
        if (!auth.Allowed) return Results.Forbid();

        var rows = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.BannedAt != null)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.UserId, u => u.Id,
                  (m, u) => new { m.UserId, UserUsername = u.Username, m.BannedById, m.BannedAt, m.BanReason })
            .ToListAsync(ct);

        var bannerIds = rows.Where(r => r.BannedById.HasValue).Select(r => r.BannedById!.Value).Distinct().ToList();
        var banners = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => bannerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var summary = rows.Select(r => new BannedMemberSummary(
            r.UserId,
            r.UserUsername,
            r.BannedById ?? Guid.Empty,
            r.BannedById.HasValue && banners.TryGetValue(r.BannedById.Value, out var bn) ? bn : null,
            r.BannedAt ?? default,
            r.BanReason)).ToList();

        return Results.Ok(summary);
    }

    private static async Task<IResult> UnbanMember(
        Guid channelId,
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        ChannelEventBroadcaster events,
        AuditLogContext audit,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var auth = AuthorizationRules.CanUnbanFromChannel(actor);
        if (!auth.Allowed) return Results.Forbid();

        var target = await db.ChannelMembers.IgnoreQueryFilters().AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);
        if (target is null || target.BannedAt is null) return Results.NotFound();

        target.Unban();
        audit.Add(
            action: "channel.unban_member",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId);
        await db.SaveChangesAsync(ct);

        var uname = (await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == userId, ct)).Username;
        await events.ChannelMemberJoined(channelId, new ChannelMemberSummary(
            userId, uname, "member", target.JoinedAt));

        return Results.NoContent();
    }
}
