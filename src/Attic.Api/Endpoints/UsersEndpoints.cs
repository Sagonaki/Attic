using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/search", Search);
        group.MapPost("/{userId:guid}/block", Block);
        group.MapDelete("/{userId:guid}/block", Unblock);

        return routes;
    }

    private static async Task<IResult> Search(
        string q,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Results.Ok(Array.Empty<UserSearchResult>());

        var me = currentUser.UserIdOrThrow;
        var prefix = q.Trim().ToLower();

        var rows = await db.Users.AsNoTracking()
            .Where(u => u.Id != me && EF.Functions.Like(u.Username.ToLower(), prefix + "%"))
            .OrderBy(u => u.Username)
            .Take(20)
            .Select(u => new UserSearchResult(u.Id, u.Username))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Block(
        Guid userId,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        Attic.Api.Hubs.FriendsEventBroadcaster events,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        if (me == userId) return Results.BadRequest(new ApiError("self_target", "Cannot block yourself."));

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (target is null) return Results.NotFound();

        var existingBlock = await db.UserBlocks.AsNoTracking()
            .AnyAsync(b => b.BlockerId == me && b.BlockedId == userId, ct);
        if (existingBlock) return Results.NoContent();

        db.UserBlocks.Add(UserBlock.Create(me, userId, clock.UtcNow));

        var (smaller, larger) = me.CompareTo(userId) < 0 ? (me, userId) : (userId, me);
        var friendship = await db.Friendships.AsTracking()
            .FirstOrDefaultAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        if (friendship is not null) db.Friendships.Remove(friendship);

        var pending = await db.FriendRequests.AsTracking()
            .Where(r => ((r.SenderId == me && r.RecipientId == userId) ||
                         (r.SenderId == userId && r.RecipientId == me)) &&
                         r.Status == FriendRequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var p in pending) p.Cancel(clock.UtcNow);

        await db.SaveChangesAsync(ct);
        await events.Blocked(userId, me);
        await events.FriendRemoved(me, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> Unblock(
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;

        var block = await db.UserBlocks.AsTracking()
            .FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == userId, ct);
        if (block is null) return Results.NotFound();

        db.UserBlocks.Remove(block);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
