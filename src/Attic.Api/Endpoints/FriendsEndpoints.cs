using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class FriendsEndpoints
{
    public static IEndpointRouteBuilder MapFriendsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/friends").RequireAuthorization();

        group.MapGet("/", ListFriends);
        group.MapDelete("/{userId:guid}", RemoveFriend);

        return routes;
    }

    private static async Task<IResult> ListFriends(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var rows = await (
            from f in db.Friendships.AsNoTracking()
            where f.UserAId == userId || f.UserBId == userId
            join u in db.Users.IgnoreQueryFilters()
                on (f.UserAId == userId ? f.UserBId : f.UserAId) equals u.Id
            orderby u.Username
            select new FriendDto(u.Id, u.Username, f.CreatedAt)
        ).ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> RemoveFriend(
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        Attic.Api.Hubs.FriendsEventBroadcaster events,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        if (me == userId) return Results.BadRequest(new ApiError("self_target", "Cannot target yourself."));

        var (smaller, larger) = me.CompareTo(userId) < 0 ? (me, userId) : (userId, me);
        var friendship = await db.Friendships.AsTracking()
            .FirstOrDefaultAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        if (friendship is null) return Results.NotFound();

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);
        await events.FriendRemoved(me, userId);
        await events.FriendRemoved(userId, me);

        return Results.NoContent();
    }
}
