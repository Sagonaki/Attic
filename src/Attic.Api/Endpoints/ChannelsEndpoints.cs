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
}
