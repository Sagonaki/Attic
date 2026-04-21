using Attic.Api.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
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

public static class PersonalChatsEndpoints
{
    public static IEndpointRouteBuilder MapPersonalChatsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/personal-chats").RequireAuthorization();
        group.MapPost("/open", Open);
        return routes;
    }

    private static async Task<IResult> Open(
        [FromBody] OpenPersonalChatRequest req,
        IValidator<OpenPersonalChatRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var me = currentUser.UserIdOrThrow;
        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (target is null) return Results.NotFound();
        if (target.Id == me) return Results.BadRequest(new ApiError("self_target", "Cannot open a personal chat with yourself."));

        var (smaller, larger) = me.CompareTo(target.Id) < 0 ? (me, target.Id) : (target.Id, me);
        var areFriends = await db.Friendships.AsNoTracking()
            .AnyAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        var hasBlock = await db.UserBlocks.AsNoTracking().AnyAsync(b =>
            (b.BlockerId == me && b.BlockedId == target.Id) ||
            (b.BlockerId == target.Id && b.BlockedId == me), ct);

        var auth = AuthorizationRules.CanOpenPersonalChat(areFriends, hasBlock);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot open personal chat."));

        // Try to find an existing personal channel whose two members are exactly {me, target}.
        var existing = await (
            from c in db.Channels.AsNoTracking()
            where c.Kind == ChannelKind.Personal
            let memberIds = db.ChannelMembers.Where(m => m.ChannelId == c.Id).Select(m => m.UserId).ToList()
            where memberIds.Count == 2 && memberIds.Contains(me) && memberIds.Contains(target.Id)
            select c).FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return Results.Ok(new ChannelDetails(
                existing.Id, "personal", existing.Name, existing.Description,
                existing.OwnerId, existing.CreatedAt, MemberCount: 2));
        }

        var channel = Channel.CreatePersonal(Guid.NewGuid(), clock.UtcNow);
        db.Channels.Add(channel);
        db.ChannelMembers.Add(ChannelMember.Join(channel.Id, me, ChannelRole.Member, clock.UtcNow));
        db.ChannelMembers.Add(ChannelMember.Join(channel.Id, target.Id, ChannelRole.Member, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        return Results.Ok(new ChannelDetails(
            channel.Id, "personal", null, null, null, channel.CreatedAt, MemberCount: 2));
    }
}
