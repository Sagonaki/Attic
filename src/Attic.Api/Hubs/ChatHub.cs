using System.Text;
using Attic.Api.Auth;
using Attic.Contracts.Messages;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Hubs;

[Authorize]
public sealed class ChatHub(AtticDbContext db, IClock clock, CurrentUser currentUser) : Hub
{
    public const string Path = "/hub";

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUser.ReadUserId(Context.User!);
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
        if (!currentUser.IsAuthenticated) return new SendMessageResponse(false, null, null, "unauthorized");

        if (string.IsNullOrWhiteSpace(request.Content))
            return new SendMessageResponse(false, null, null, "empty_content");
        if (Encoding.UTF8.GetByteCount(request.Content) > Message.MaxContentBytes)
            return new SendMessageResponse(false, null, null, "content_too_large");

        var member = await db.ChannelMembers
            .IgnoreQueryFilters()  // we want banned rows too so we can report the correct reason
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == request.ChannelId && m.UserId == currentUser.UserIdOrThrow);

        // Phase 1 fallback: the seeded lobby has no members yet; auto-join on first post.
        // Do NOT auto-join banned members (BannedAt != null means banned).
        if (member is null)
        {
            var channelExists = await db.Channels.AnyAsync(c => c.Id == request.ChannelId);
            if (!channelExists) return new SendMessageResponse(false, null, null, "channel_not_found");

            var auto = ChannelMember.Join(request.ChannelId, currentUser.UserIdOrThrow, ChannelRole.Member, clock.UtcNow);
            db.ChannelMembers.Add(auto);
            member = auto;
        }

        var auth = AuthorizationRules.CanPostInChannel(member);
        if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());

        var msg = Message.Post(request.ChannelId, currentUser.UserIdOrThrow, request.Content, request.ReplyToId, clock.UtcNow);
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == currentUser.UserIdOrThrow);
        var dto = new MessageDto(msg.Id, msg.ChannelId, msg.SenderId, sender.Username, msg.Content, msg.ReplyToId, msg.CreatedAt, null);

        await Clients.Group(GroupNames.Channel(msg.ChannelId)).SendAsync("MessageCreated", dto);

        return new SendMessageResponse(true, msg.Id, msg.CreatedAt, null);
    }

    public async Task<object> SubscribeToChannel(Guid channelId)
    {
        if (!currentUser.IsAuthenticated) return new { ok = false, error = "unauthorized" };

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
}

public static class GroupNames
{
    public static string User(Guid userId) => $"User_{userId:N}";
    public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
    public static string Channel(Guid channelId) => $"Channel_{channelId:N}";
}
