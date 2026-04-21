using Attic.Api.Auth;
using Attic.Contracts.Messages;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    AtticDbContext db,
    IClock clock,
    IValidator<SendMessageRequest> sendMessageValidator) : Hub
{
    public const string Path = "/hub";

    // The scoped CurrentUser is populated by AtticAuthenticationHandler on HTTP requests
    // only; SignalR invocation scopes don't go through it, so we read the user id from
    // Context.User directly here.
    private Guid? UserId => CurrentUser.ReadUserId(Context.User!);

    public override async Task OnConnectedAsync()
    {
        var userId = UserId;
        var sessionId = CurrentUser.ReadSessionId(Context.User!);
        if (userId is null || sessionId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId.Value));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Session(sessionId.Value));
        await base.OnConnectedAsync();
    }

    public async Task<SendMessageResponse> SendMessage(SendMessageRequest request)
    {
        var userId = UserId;
        if (userId is null) return new SendMessageResponse(false, null, null, "unauthorized");

        var validation = await sendMessageValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return new SendMessageResponse(false, null, null, validation.Errors[0].ErrorCode);

        var member = await db.ChannelMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == request.ChannelId && m.UserId == userId.Value);

        var auth = AuthorizationRules.CanPostInChannel(member);
        if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());

        var msg = Message.Post(request.ChannelId, userId.Value, request.Content, request.ReplyToId, clock.UtcNow);
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId.Value);
        var dto = new MessageDto(msg.Id, msg.ChannelId, msg.SenderId, sender.Username, msg.Content, msg.ReplyToId, msg.CreatedAt, null);

        await Clients.Group(GroupNames.Channel(msg.ChannelId)).SendAsync("MessageCreated", dto);

        return new SendMessageResponse(true, msg.Id, msg.CreatedAt, null);
    }

    public async Task<object> SubscribeToChannel(Guid channelId)
    {
        if (UserId is null) return new { ok = false, error = "unauthorized" };

        var channelExists = await db.Channels.AnyAsync(c => c.Id == channelId);
        if (!channelExists) return new { ok = false, error = "channel_not_found" };

        // Phase 1: any authenticated user may subscribe to any existing channel.
        // Phase 2 replaces this with a membership check.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Channel(channelId));
        return new { ok = true };
    }

    public async Task UnsubscribeFromChannel(Guid channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.Channel(channelId));
    }

    public async Task<object> DeleteMessage(long messageId)
    {
        var userId = UserId;
        if (userId is null) return new { ok = false, error = "unauthorized" };

        var msg = await db.Messages.AsTracking().FirstOrDefaultAsync(m => m.Id == messageId);
        if (msg is null) return new { ok = false, error = "not_found" };

        var channel = await db.Channels.AsNoTracking().FirstAsync(c => c.Id == msg.ChannelId);
        var membership = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == msg.ChannelId && m.UserId == userId.Value);

        var auth = AuthorizationRules.CanDeleteMessage(msg, userId.Value, membership, channel.Kind);
        if (!auth.Allowed) return new { ok = false, error = auth.Reason.ToString() };

        msg.SoftDelete(clock.UtcNow);
        await db.SaveChangesAsync();

        await Clients.Group(GroupNames.Channel(msg.ChannelId)).SendAsync("MessageDeleted", msg.ChannelId, msg.Id);
        return new { ok = true };
    }
}

public static class GroupNames
{
    public static string User(Guid userId) => $"User_{userId:N}";
    public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
    public static string Channel(Guid channelId) => $"Channel_{channelId:N}";
}
