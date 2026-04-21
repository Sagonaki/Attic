using Attic.Api.Auth;
using Attic.Contracts.Attachments;
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
    IValidator<SendMessageRequest> sendMessageValidator,
    IValidator<EditMessageRequest> editValidator) : Hub
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

        // Personal-chat: friendship + no-block gate.
        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId);
        if (channel is { Kind: Attic.Domain.Enums.ChannelKind.Personal })
        {
            var otherId = await db.ChannelMembers.AsNoTracking()
                .Where(m => m.ChannelId == request.ChannelId && m.UserId != userId.Value)
                .Select(m => m.UserId)
                .FirstOrDefaultAsync();
            if (otherId == Guid.Empty)
                return new SendMessageResponse(false, null, null, "invalid_channel");

            var (smaller, larger) = userId.Value.CompareTo(otherId) < 0
                ? (userId.Value, otherId)
                : (otherId, userId.Value);
            var areFriends = await db.Friendships.AsNoTracking()
                .AnyAsync(f => f.UserAId == smaller && f.UserBId == larger);
            var hasBlock = await db.UserBlocks.AsNoTracking().AnyAsync(b =>
                (b.BlockerId == userId.Value && b.BlockedId == otherId) ||
                (b.BlockerId == otherId && b.BlockedId == userId.Value));

            var personalAuth = AuthorizationRules.CanPostInPersonalChat(areFriends, hasBlock);
            if (!personalAuth.Allowed)
                return new SendMessageResponse(false, null, null, personalAuth.Reason.ToString());
        }

        var msg = Message.Post(request.ChannelId, userId.Value, request.Content, request.ReplyToId, clock.UtcNow);
        db.Messages.Add(msg);
        await db.SaveChangesAsync();   // Populates message.Id.

        if (request.AttachmentIds is { Length: > 0 })
        {
            var attachmentIds = request.AttachmentIds;
            var attachments = await db.Attachments.AsTracking()
                .Where(a => attachmentIds.Contains(a.Id)
                            && a.UploaderId == userId.Value
                            && a.MessageId == null)
                .ToListAsync();
            if (attachments.Count != attachmentIds.Length)
                return new SendMessageResponse(false, null, null, "invalid_attachments");

            foreach (var a in attachments) a.BindToMessage(msg.Id);
            await db.SaveChangesAsync();
        }

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId.Value);

        var attachmentDtos = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId == msg.Id)
            .Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.Comment))
            .ToArrayAsync();

        var dto = new MessageDto(msg.Id, msg.ChannelId, msg.SenderId, sender.Username, msg.Content, msg.ReplyToId, msg.CreatedAt, null,
            attachmentDtos.Length > 0 ? attachmentDtos : null);

        await Clients.Group(GroupNames.Channel(msg.ChannelId)).SendAsync("MessageCreated", dto);

        return new SendMessageResponse(true, msg.Id, msg.CreatedAt, null);
    }

    public async Task<object> SubscribeToChannel(Guid channelId)
    {
        var userId = UserId;
        if (userId is null) return new { ok = false, error = "unauthorized" };

        var channelExists = await db.Channels.AnyAsync(c => c.Id == channelId);
        if (!channelExists) return new { ok = false, error = "channel_not_found" };

        var isMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId.Value);
        if (!isMember) return new { ok = false, error = "not_a_member" };

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

    public async Task<EditMessageResponse> EditMessage(EditMessageRequest request)
    {
        var userId = UserId;
        if (userId is null) return new EditMessageResponse(false, null, "unauthorized");

        var vr = await editValidator.ValidateAsync(request);
        if (!vr.IsValid) return new EditMessageResponse(false, null, vr.Errors[0].ErrorCode);

        var msg = await db.Messages.AsTracking().FirstOrDefaultAsync(m => m.Id == request.MessageId);
        if (msg is null) return new EditMessageResponse(false, null, "not_found");

        var auth = AuthorizationRules.CanEditMessage(msg, userId.Value);
        if (!auth.Allowed) return new EditMessageResponse(false, null, auth.Reason.ToString());

        msg.Edit(request.Content, clock.UtcNow);
        await db.SaveChangesAsync();

        await Clients.Group(GroupNames.Channel(msg.ChannelId))
            .SendAsync("MessageEdited", msg.ChannelId, msg.Id, msg.Content, msg.UpdatedAt);

        return new EditMessageResponse(true, msg.UpdatedAt, null);
    }
}

public static class GroupNames
{
    public static string User(Guid userId) => $"User_{userId:N}";
    public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
    public static string Channel(Guid channelId) => $"Channel_{channelId:N}";
}
