using Attic.Api.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class ChannelsEndpoints
{
    public static IEndpointRouteBuilder MapChannelsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/channels").RequireAuthorization();

        group.MapPost("/", CreateChannel);
        group.MapGet("/public", GetPublicCatalog).AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> CreateChannel(
        [FromBody] CreateChannelRequest req,
        IValidator<CreateChannelRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var kind = req.Kind == "public" ? ChannelKind.Public : ChannelKind.Private;
        var trimmedName = req.Name.Trim();

        var nameTaken = await db.Channels
            .AnyAsync(c => c.Name == trimmedName && c.Kind != ChannelKind.Personal, ct);
        if (nameTaken) return Results.Conflict(new ApiError("name_taken", "Channel name is already taken."));

        var channel = Channel.CreateRoom(Guid.NewGuid(), kind, trimmedName, req.Description, currentUser.UserIdOrThrow, clock.UtcNow);
        db.Channels.Add(channel);

        var ownerMember = ChannelMember.Join(channel.Id, currentUser.UserIdOrThrow, ChannelRole.Owner, clock.UtcNow);
        db.ChannelMembers.Add(ownerMember);

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ChannelDetails(
            channel.Id, kind.ToString().ToLowerInvariant(),
            channel.Name, channel.Description, channel.OwnerId, channel.CreatedAt,
            MemberCount: 1));
    }

    private record PublicCatalogItem(Guid Id, string Name, string? Description, int MemberCount);

    private static async Task<IResult> GetPublicCatalog(
        string? search,
        string? cursor,
        int? limit,
        AtticDbContext db,
        CancellationToken ct)
    {
        var size = Math.Clamp(limit ?? 30, 1, 100);

        var query = db.Channels.AsNoTracking()
            .Where(c => c.Kind == ChannelKind.Public);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => EF.Functions.Like(c.Name!.ToLower(), s + "%"));
        }

        if (!string.IsNullOrEmpty(cursor) && Guid.TryParse(cursor, out var cursorId))
        {
            query = query.Where(c => c.Id.CompareTo(cursorId) > 0);
        }

        var rows = await query
            .OrderBy(c => c.Id)
            .Take(size)
            .Select(c => new { c.Id, Name = c.Name!, c.Description })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.ChannelMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ChannelId))
            .GroupBy(m => m.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.ChannelId, c => c.Count);

        var items = rows.Select(r => new PublicCatalogItem(
            r.Id, r.Name, r.Description,
            countMap.TryGetValue(r.Id, out var n) ? n : 0
        )).ToList();

        var nextCursor = rows.Count == size ? rows[^1].Id.ToString("D") : null;
        return Results.Ok(new PagedResult<PublicCatalogItem>(items, nextCursor));
    }
}
