using Attic.Api.Auth;
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

public static class FriendRequestsEndpoints
{
    public static IEndpointRouteBuilder MapFriendRequestsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/friend-requests").RequireAuthorization();

        group.MapPost("/", Send);
        group.MapGet("/", ListMine);
        group.MapPost("/{id:guid}/accept", Accept);
        group.MapPost("/{id:guid}/decline", Decline);

        return routes;
    }

    private static async Task<IResult> Send(
        [FromBody] SendFriendRequestRequest req,
        IValidator<SendFriendRequestRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var senderId = currentUser.UserIdOrThrow;
        var recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (recipient is null) return Results.NotFound();
        if (recipient.Id == senderId)
            return Results.BadRequest(new ApiError("self_request", "Cannot send a friend request to yourself."));

        var (smaller, larger) = senderId.CompareTo(recipient.Id) < 0 ? (senderId, recipient.Id) : (recipient.Id, senderId);
        var areFriends = await db.Friendships.AsNoTracking()
            .AnyAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);

        var hasPending = await db.FriendRequests.AsNoTracking().AnyAsync(r =>
            ((r.SenderId == senderId && r.RecipientId == recipient.Id) ||
             (r.SenderId == recipient.Id && r.RecipientId == senderId)) &&
            r.Status == FriendRequestStatus.Pending, ct);

        var hasBlock = await db.UserBlocks.AsNoTracking().AnyAsync(b =>
            (b.BlockerId == senderId && b.BlockedId == recipient.Id) ||
            (b.BlockerId == recipient.Id && b.BlockedId == senderId), ct);

        var auth = AuthorizationRules.CanSendFriendRequest(areFriends, hasPending, hasBlock);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot send friend request."));

        var friendReq = FriendRequest.Issue(Guid.NewGuid(), senderId, recipient.Id, req.Text, clock.UtcNow);
        db.FriendRequests.Add(friendReq);
        await db.SaveChangesAsync(ct);

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == senderId, ct);
        return Results.Ok(new FriendRequestDto(
            friendReq.Id, senderId, sender.Username, recipient.Id, recipient.Username,
            friendReq.Text, friendReq.Status.ToString().ToLowerInvariant(),
            friendReq.CreatedAt, friendReq.DecidedAt));
    }

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var rows = await db.FriendRequests.AsNoTracking()
            .Where(r => (r.SenderId == userId || r.RecipientId == userId) &&
                         r.Status == FriendRequestStatus.Pending)
            .Join(db.Users.IgnoreQueryFilters(), r => r.SenderId, u => u.Id, (r, u) => new { Req = r, SenderName = u.Username })
            .Join(db.Users.IgnoreQueryFilters(), x => x.Req.RecipientId, u => u.Id,
                  (x, u) => new FriendRequestDto(
                      x.Req.Id, x.Req.SenderId, x.SenderName,
                      x.Req.RecipientId, u.Username, x.Req.Text,
                      x.Req.Status.ToString().ToLowerInvariant(),
                      x.Req.CreatedAt, x.Req.DecidedAt))
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

        var req = await db.FriendRequests.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientId == userId, ct);
        if (req is null) return Results.NotFound();
        if (req.Status != FriendRequestStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Friend request is no longer pending."));

        req.Accept(clock.UtcNow);
        db.Friendships.Add(Friendship.Create(req.SenderId, req.RecipientId, clock.UtcNow));
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

        var req = await db.FriendRequests.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientId == userId, ct);
        if (req is null) return Results.NotFound();
        if (req.Status != FriendRequestStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Friend request is no longer pending."));

        req.Decline(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
