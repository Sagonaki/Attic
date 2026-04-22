using System.Buffers;
using Attic.Api.Auth;
using Attic.Api.RateLimiting;
using Attic.Contracts.Attachments;
using Attic.Contracts.Messages;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Services;
using Attic.Infrastructure.Audit;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.UnreadCounts;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ObjectPool;

namespace Attic.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    AtticDbContext db,
    IClock clock,
    IValidator<SendMessageRequest> sendMessageValidator,
    IValidator<EditMessageRequest> editValidator,
    Attic.Infrastructure.Presence.IPresenceStore presenceStore,
    AuditLogContext audit,
    HubRateLimiter rateLimiter,
    IUnreadCountStore unreadCounts,
    IMessageFanoutQueue fanoutQueue,
    ObjectPool<MessageFanoutWorkItem> fanoutPool) : Hub
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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = UserId;
        if (userId is not null)
        {
            await presenceStore.RemoveConnectionAsync(userId.Value, Context.ConnectionId, Context.ConnectionAborted);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<SendMessageResponse> SendMessage(SendMessageRequest request)
    {
        var userId = UserId;
        if (userId is null) return new SendMessageResponse(false, null, null, "unauthorized");

        if (!rateLimiter.TryAcquire(userId.Value, clock.UtcNow))
            return new SendMessageResponse(false, null, null, "rate_limited");

        var validation = await sendMessageValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return new SendMessageResponse(false, null, null, validation.Errors[0].ErrorCode);

        // One round-trip for the channel's kind plus the caller's membership row
        // (ban rows included so CanPostInChannel can reject them cleanly).
        var pre = await (from c in db.Channels.AsNoTracking()
                         where c.Id == request.ChannelId
                         from m in db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
                             .Where(mm => mm.ChannelId == c.Id && mm.UserId == userId.Value)
                             .DefaultIfEmpty()
                         select new { c.Kind, Member = m })
                        .FirstOrDefaultAsync();

        var auth = AuthorizationRules.CanPostInChannel(pre?.Member);
        if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());

        // Personal-chat: friendship + no-block gate.
        if (pre is { Kind: Attic.Domain.Enums.ChannelKind.Personal })
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

        AttachmentDto[]? attachmentDtos = null;
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

            // Project from the tracked rows — the persisted state matches in-memory after SaveChanges.
            attachmentDtos = attachments.Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.Comment)).ToArray();
        }

        // One round-trip for sender username + member roster. IgnoreQueryFilters so the
        // sender row is present even if concurrently banned; banned peers are filtered
        // out in memory so fan-out skips them.
        var memberships = await (from m in db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
                                 join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                                 where m.ChannelId == request.ChannelId
                                 select new { m.UserId, u.Username, IsBanned = m.BannedAt != null })
                                .ToListAsync();

        var senderUsername = memberships.First(x => x.UserId == userId.Value).Username;

        // Rent a Guid[] from the shared ArrayPool — length ≥ memberships.Count. We fill only the
        // first memberCount entries; the drain loop reads MemberCount, then returns the array.
        var memberIdsArray = ArrayPool<Guid>.Shared.Rent(memberships.Count);
        int memberCount = 0;
        foreach (var m in memberships)
        {
            if (m.UserId != userId.Value && !m.IsBanned)
                memberIdsArray[memberCount++] = m.UserId;
        }

        var dto = new MessageDto(msg.Id, msg.ChannelId, msg.SenderId, senderUsername, msg.Content, msg.ReplyToId, msg.CreatedAt, null,
            attachmentDtos);

        var work = fanoutPool.Get();
        work.ChannelId = msg.ChannelId;
        work.Message = dto;
        work.MemberIds = memberIdsArray;
        work.MemberCount = memberCount;
        if (!fanoutQueue.TryEnqueue(work))
        {
            // Channel completed on host shutdown — release the rented buffers instead of leaking.
            ArrayPool<Guid>.Shared.Return(memberIdsArray, clearArray: false);
            fanoutPool.Return(work);
        }

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

    public async Task<object> UnsubscribeFromChannel(Guid channelId)
    {
        var userId = UserId;
        if (userId is null) return new { ok = false, error = "unauthorized" };

        // Mirror SubscribeToChannel: only let a real member toggle their own channel
        // subscription. Without this check an authenticated user can dial group
        // membership on arbitrary channel IDs and generate noise for other clients.
        var isMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId.Value);
        if (!isMember) return new { ok = false, error = "not_a_member" };

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.Channel(channelId));
        return new { ok = true };
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
        if (msg.SenderId != userId.Value)
        {
            audit.Add(
                action: "message.admin_delete",
                actorUserId: userId.Value,
                targetChannelId: msg.ChannelId,
                targetUserId: msg.SenderId,
                targetMessageId: msg.Id);
        }
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

    public async Task Heartbeat(string state)
    {
        var userId = UserId;
        if (userId is null) return;

        if (state != "active" && state != "idle") return;

        var tabState = state == "active"
            ? Attic.Infrastructure.Presence.TabState.Active
            : Attic.Infrastructure.Presence.TabState.Idle;

        var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
        await presenceStore.WriteHeartbeatAsync(userId.Value, Context.ConnectionId, tabState, nowMs, Context.ConnectionAborted);
    }

    public async Task<object> MarkRead(Guid channelId, long lastMessageId)
    {
        var userId = UserId;
        if (userId is null) return new { ok = false, error = "unauthorized" };

        // Without a membership check, any authenticated user could upsert a ChannelRead
        // row for an arbitrary channel GUID and fire UnreadChanged on their own group.
        // That's noise, not a data leak, but it's still unintended writes — gate it.
        var isMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId.Value);
        if (!isMember) return new { ok = false, error = "not_a_member" };

        var existing = await db.ChannelReads.AsTracking()
            .FirstOrDefaultAsync(r => r.ChannelId == channelId && r.UserId == userId.Value);
        if (existing is null)
        {
            db.ChannelReads.Add(Attic.Domain.Entities.ChannelRead.Create(channelId, userId.Value, lastMessageId, clock.UtcNow));
        }
        else
        {
            existing.MarkRead(lastMessageId, clock.UtcNow);
        }
        await db.SaveChangesAsync();

        await unreadCounts.ResetAsync(userId.Value, channelId, default);

        await Clients.Group(GroupNames.User(userId.Value))
            .SendAsync("UnreadChanged", channelId, 0);

        return new { ok = true };
    }
}

public static class GroupNames
{
    public static string User(Guid userId) => $"User_{userId:N}";
    public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
    public static string Channel(Guid channelId) => $"Channel_{channelId:N}";
}
