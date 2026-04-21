using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Invitations;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class InvitationsEndpoints
{
    public static IEndpointRouteBuilder MapInvitationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var channels = routes.MapGroup("/api/channels/{channelId:guid}").RequireAuthorization();
        channels.MapPost("/invitations", Issue);

        var invites = routes.MapGroup("/api/invitations").RequireAuthorization();
        invites.MapGet("/", ListMine);
        invites.MapPost("/{id:guid}/accept", Accept);
        invites.MapPost("/{id:guid}/decline", Decline);

        return routes;
    }

    private static async Task<IResult> Issue(
        Guid channelId,
        [FromBody] InviteToChannelRequest req,
        IValidator<InviteToChannelRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null) return Results.NotFound();

        var inviterId = currentUser.UserIdOrThrow;
        var inviter = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == inviterId, ct);

        var invitee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (invitee is null) return Results.NotFound();
        if (invitee.Id == inviterId) return Results.BadRequest(new ApiError("self_invite", "Cannot invite yourself."));

        var inviteeMembership = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == invitee.Id, ct);

        var hasPending = await db.ChannelInvitations.AsNoTracking()
            .AnyAsync(i => i.ChannelId == channelId && i.InviteeId == invitee.Id && i.Status == InvitationStatus.Pending, ct);

        var auth = AuthorizationRules.CanInviteToChannel(channel, inviter, inviteeMembership, hasPending);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot invite."));

        var inv = ChannelInvitation.Issue(Guid.NewGuid(), channelId, inviterId, invitee.Id, clock.UtcNow);
        db.ChannelInvitations.Add(inv);
        await db.SaveChangesAsync(ct);

        var inviterUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == inviterId, ct);
        return Results.Ok(new InvitationDto(
            inv.Id, channelId, channel.Name ?? "", inviterId,
            inviterUser.Username,
            inv.Status.ToString().ToLowerInvariant(), inv.CreatedAt, inv.DecidedAt));
    }

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var rows = await db.ChannelInvitations.AsNoTracking()
            .Where(i => i.InviteeId == userId && i.Status == InvitationStatus.Pending)
            .Join(db.Channels.AsNoTracking(), i => i.ChannelId, c => c.Id, (i, c) => new { Inv = i, Channel = c })
            .Join(db.Users.IgnoreQueryFilters(), x => x.Inv.InviterId, u => u.Id,
                  (x, u) => new InvitationDto(
                      x.Inv.Id, x.Channel.Id, x.Channel.Name ?? "",
                      x.Inv.InviterId, u.Username,
                      x.Inv.Status.ToString().ToLowerInvariant(),
                      x.Inv.CreatedAt, x.Inv.DecidedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Accept(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var inv = await db.ChannelInvitations.AsTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.InviteeId == userId, ct);
        if (inv is null) return Results.NotFound();

        if (inv.Status != InvitationStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Invitation is no longer pending."));

        inv.Accept(clock.UtcNow);
        var existing = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.ChannelId == inv.ChannelId && m.UserId == userId, ct);
        if (!existing)
        {
            db.ChannelMembers.Add(ChannelMember.Join(inv.ChannelId, userId, ChannelRole.Member, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> Decline(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var inv = await db.ChannelInvitations.AsTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.InviteeId == userId, ct);
        if (inv is null) return Results.NotFound();

        if (inv.Status != InvitationStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Invitation is no longer pending."));

        inv.Decline(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
