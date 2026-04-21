using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class MessagesEndpoints
{
    public static IEndpointRouteBuilder MapMessagesEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/channels/{channelId:guid}/messages", GetBeforeCursor).RequireAuthorization();
        return routes;
    }

    private static async Task<IResult> GetBeforeCursor(
        Guid channelId,
        string? before,
        int? limit,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        // Membership (non-banned) required to read a channel's message history.
        var isMember = await db.ChannelMembers
            .AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == currentUser.UserIdOrThrow, ct);
        if (!isMember) return Results.Forbid();

        var size = Math.Clamp(limit ?? 50, 1, 200);

        var query = db.Messages.AsNoTracking()
            .Where(m => m.ChannelId == channelId);

        if (KeysetCursor.TryDecode(before, out var cursor))
        {
            query = query.Where(m => m.Id < cursor);
        }

        var rows = await query
            .OrderByDescending(m => m.Id)
            .Take(size)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.SenderId,
                  u => u.Id,
                  (m, u) => new MessageDto(m.Id, m.ChannelId, m.SenderId, u.Username, m.Content, m.ReplyToId, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);

        string? nextCursor = rows.Count == size ? KeysetCursor.Encode(rows[^1].Id) : null;
        return Results.Ok(new PagedResult<MessageDto>(rows, nextCursor));
    }
}
