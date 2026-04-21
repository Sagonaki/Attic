# Attic — Web Chat Server Design

**Status:** approved (brainstorming phase)
**Date:** 2026-04-21
**Source inputs:** `Preparation work/md/initial task.md`, `Preparation work/md/tech nuances.md`, `FE prototype/` (React 19 / Vite / Tailwind prototype)

## 1. Overview

Attic is a classic web-based chat application supporting user registration and cookie-based authentication, public and private chat rooms, 1:1 personal messaging gated by friendship, contacts with user-to-user blocking, file and image attachments, basic room moderation, and persistent message history. Target scale is 300 concurrent users, up to 1 000 members per room, and rooms containing 10 000+ messages remain responsive.

The stack is .NET 10 + ASP.NET Core + SignalR on the backend, PostgreSQL via EF Core for persistence, Redis for the SignalR backplane and live presence, the local filesystem for attachments, and the existing React 19 / Vite / Tailwind prototype (with TanStack Query + a SignalR client wrapper bolted on) as the frontend. Local development and production orchestration use .NET Aspire.

## 2. Non-goals

- Real outbound email. "Forgot password" is stubbed: the reset URL is written to the application log rather than sent (§4.6). Hookable later with one-line swap.
- End-to-end encryption, federation, external OAuth sign-in, mobile clients.
- Server-side image thumbnailing, full-text message search, message reactions, read receipts beyond unread counters, typing indicators.
- Horizontal scale beyond a single API instance. Redis + backplane are in place so multi-node is a future option, but the initial prod deployment is one instance.

## 3. High-level architecture

```
[ Browser (React SPA) ]
   │  HTTPS
   ▼
[ ASP.NET Core API (.NET 10, Kestrel, Server GC) ]
   ├── REST controllers under /api/*
   ├── SignalR hub at /hub
   └── Static SPA from wwwroot (prod) / Vite dev server (dev)
        │
        ├──► PostgreSQL   — source of truth (users, channels, messages, sessions, friends, attachments-metadata, audit log)
        ├──► Redis        — SignalR backplane + presence (tab heartbeats) + hot unread-count cache
        └──► Local FS     — /data/attachments, mounted as a docker volume
```

### 3.1 Project layout (single `.slnx` solution)

```
src/
├── Attic.ServiceDefaults/      # OTel, health checks, resilience, service discovery
├── Attic.AppHost/              # Aspire orchestration
├── Attic.Api/                  # controllers, hub, auth, startup
├── Attic.Domain/               # entities, value objects, domain services (authorization, presence, cursors)
├── Attic.Infrastructure/       # EF Core DbContext, migrations, repositories, Redis, filesystem storage
├── Attic.Contracts/            # DTOs shared with clients (NSwag → TypeScript at build time)
├── Attic.Web/                  # Vite + React SPA (refactored prototype)
tests/
├── Attic.Domain.Tests/         # unit tests
├── Attic.Api.IntegrationTests/ # Aspire + TestContainers integration tests
└── Attic.Web.E2E/              # Playwright golden-path tests
```

`Attic.Domain` has no EF Core dependency — rules are exercised in milliseconds by unit tests. `Attic.Infrastructure` references `Attic.Domain`. `Attic.Api` references both and hosts the hub + controllers. Central Package Management via `Directory.Packages.props`.

### 3.2 Deployment

Aspire AppHost orchestrates Postgres + Redis + API + Vite in dev. Production ships as one API container that also serves the built SPA from `wwwroot`, plus Postgres and Redis containers; the docker-compose file is generated from the Aspire manifest. `KestrelServerOptions.Limits.MaxConcurrentConnections` = 2 048; `MaxConcurrentUpgradedConnections` = 2 048. Server GC enabled.

## 4. Data model

### 4.1 Entities

| Entity | Keys and salient columns | Notes |
|---|---|---|
| `User` | `Id UUID v7` (PK), `Email` (unique), `Username` (unique, immutable), `PasswordHash`, `CreatedAt`, `DeletedAt` | Soft-deleted. On delete, `Email`/`Username` rewritten to `deleted-{id}@void` to free the unique index. Global query filter `DeletedAt IS NULL`. |
| `Session` | `Id UUID` (PK), `UserId`, `TokenHash` (SHA-256 of a 256-bit random secret), `UserAgent`, `Ip`, `CreatedAt`, `LastSeenAt`, `ExpiresAt`, `RevokedAt` | Cookie value is `{Id}.{TokenPart}`. Sliding expiration = 30 days. `LastSeenAt` updated asynchronously, throttled to once per 30 s per session. |
| `Channel` | `Id UUID v7` (PK), `Kind` (`public`/`private`/`personal`), `Name`, `Description`, `OwnerId`, `CreatedAt`, `DeletedAt` | Polymorphic: personal dialogs are channels of `Kind = personal` with two members. `Name` and `OwnerId` are null when `Kind = personal`. Global query filter `DeletedAt IS NULL`. Filtered unique index on `Name` where `Kind <> 'personal' AND DeletedAt IS NULL`. |
| `ChannelMember` | `(ChannelId, UserId)` PK, `Role` (`owner`/`admin`/`member`), `JoinedAt`, `BannedAt`, `BannedById`, `BanReason` | Row persists after ban (supports "view ban list / who banned whom"). Global query filter hides `BannedAt IS NOT NULL` unless `.IgnoreQueryFilters()`. |
| `ChannelInvitation` | `Id`, `ChannelId`, `InviteeId`, `InviterId`, `Status` (`pending`/`accepted`/`declined`), `CreatedAt`, `DecidedAt` | Private rooms only. |
| `Message` | `Id BIGINT identity` (PK), `ChannelId`, `SenderId`, `Content TEXT` (CHECK `length(Content) <= 3072`), `ReplyToId` (nullable FK self), `CreatedAt`, `UpdatedAt`, `DeletedAt` | `bigint identity` chosen for monotonic keyset cursors. `UpdatedAt` set on edit ("edited" indicator). `DeletedAt` soft-delete. |
| `Attachment` | `Id UUID v7`, `MessageId` (nullable until bound), `UploaderId`, `OriginalFileName`, `ContentType`, `SizeBytes`, `StoragePath`, `Comment`, `CreatedAt` | `StoragePath` = `yyyy/mm/dd/{sha256}.bin`, content-addressable (natural dedupe). |
| `FriendRequest` | `Id`, `SenderId`, `RecipientId`, `Text`, `Status`, `CreatedAt`, `DecidedAt` | Unique filtered index on `(SenderId, RecipientId)` where `Status = 'pending'`. |
| `Friendship` | `(UserAId, UserBId)` PK where `A < B`, `CreatedAt` | Canonical unordered pair, single row. |
| `UserBlock` | `(BlockerId, BlockedId)` PK, `CreatedAt` | Asymmetric. |
| `ChannelRead` | `(ChannelId, UserId)` PK, `LastReadMessageId`, `UpdatedAt` | Unread count derived as `COUNT(*) WHERE Id > LastReadMessageId`. Hot-cached in Redis. |
| `PasswordResetToken` | `Id`, `UserId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `UsedAt` | Stubbed flow; token URL logged, not emailed. Single-use, 1 h TTL. |
| `AuditLog` | `Id`, `ActorUserId`, `Action`, `TargetChannelId`, `TargetUserId`, `TargetMessageId`, `Data jsonb`, `CreatedAt` | Emitted by `SaveChangesInterceptor` for admin/ban/delete/role-change actions. |

### 4.2 Indexes

- `Message (ChannelId, Id DESC)` — the critical infinite-scroll keyset index. No separate `ChannelId`-only index.
- `Message (SenderId, Id DESC)` — "my messages" lookups for edit/delete authorization.
- `User (Email)`, `User (Username)` — unique.
- `ChannelMember (ChannelId, UserId)` composite PK (authorization hot path).
- `ChannelMember (UserId)` — "list my channels".
- `Session (UserId)` filtered `WHERE RevokedAt IS NULL AND ExpiresAt > now()` — active sessions.
- `Channel` filtered unique on `Name` where `Kind <> 'personal' AND DeletedAt IS NULL`; covering `INCLUDE (Description)` for the catalog so name+description lookups return directly from the index. Member counts are computed from `ChannelMember` at query time; denormalization is deferred until it is shown to matter.
- `FriendRequest (RecipientId, Status)` — incoming-requests list.
- `ChannelInvitation (InviteeId, Status)` — incoming invitations.
- No index on `IsEdited`, `Kind`, or any frequently-updated timestamp.

### 4.3 EF Core patterns

- `AsNoTracking()` default for reads (catalog, history, profile, lists).
- `AsSplitQuery()` for "channel with members + recent messages" loads to avoid Cartesian explosion.
- Global query filters on `User.DeletedAt`, `Channel.DeletedAt`, `Message.DeletedAt`, `ChannelMember.BannedAt`.
- `SaveChangesInterceptor` sets `UpdatedAt` timestamps and emits `AuditLog` rows for tracked admin actions.
- Keyset pagination: `WHERE ChannelId = @id AND Id < @cursor ORDER BY Id DESC LIMIT 50`. Never `OFFSET`.
- `IAsyncEnumerable` for streaming large result sets (history, catalog) to the controller response; DTO projection (`MessageDto`) rather than materializing full entities.

## 5. Authentication and sessions

### 5.1 Cookie mechanics

- Name `attic.session`; `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`.
- Value `{SessionId}.{TokenPart}` (Base64Url). 30-day sliding expiration.
- Custom `AuthenticationHandler` parses cookie, looks up `Session` by `SessionId` (indexed), timing-safe-compares `SHA-256(TokenPart) == Session.TokenHash`, rejects if `RevokedAt` set or `ExpiresAt` passed, else sets `ClaimsPrincipal` with `UserId`.
- `LastSeenAt` updated fire-and-forget, throttled (once per 30 s per session) so writes don't amplify.

### 5.2 Registration

`POST /api/auth/register` with `email`, `username`, `password`. Uniqueness enforced at the database. Password hashed with ASP.NET Core `PasswordHasher<T>` (PBKDF2-SHA512, 100 k iterations). No email verification; registration auto-signs-in by creating a `Session` + cookie.

### 5.3 Sign-in / sign-out

`POST /api/auth/login` verifies password, creates `Session`, sets cookie. `POST /api/auth/logout` sets `RevokedAt` on the current session, clears cookie. Other sessions unaffected.

### 5.4 Active sessions

`GET /api/sessions` returns all active sessions for the current user. `DELETE /api/sessions/{id}` revokes one. When any session is revoked, the server broadcasts `ForceLogout(sessionId)` to the `Session_{id}` SignalR group; the client tab drops to the login screen. Sockets are not force-closed — the next authenticated request fails naturally.

### 5.5 Password change

`POST /api/auth/change-password` (authenticated): verifies current password, rehashes, optionally revokes all other sessions (UI checkbox).

### 5.6 Password reset (stubbed)

`POST /api/auth/password/forgot` always returns 200 (no user enumeration). If the email matches, a `PasswordResetToken` is created and the reset URL is written to the application log via `ILogger` (visible in Aspire dashboard). `POST /api/auth/password/reset` validates and consumes the token, rehashes, revokes all other sessions. The "send email" call is behind an `IEmailSender` with a `LoggingEmailSender` implementation; swap for SMTP later without changing flow code.

### 5.7 Account deletion

`POST /api/auth/delete-account` (authenticated, requires password confirmation). Single transaction:

- Soft-delete the `User`, rewrite `Email`/`Username` to tombstones.
- Hard-delete `Channels` where `OwnerId = self`; cascade `Messages`, `Attachments` (metadata; filesystem cleaned by ref-counted sweeper — §7.4), `ChannelMember`, `ChannelInvitation`, `ChannelRead`.
- Delete `ChannelMember` rows for non-owned channels (user leaves).
- Delete `Friendship`, `FriendRequest`, `UserBlock`, `Session` rows.
- Messages the user sent in *other* users' personal dialogs and in rooms they did not own are preserved; the sender resolves as "deleted user" via the FK to the soft-deleted `User`.

### 5.8 SignalR handshake

Client connects to `/hub` with the session cookie; the same `AuthenticationHandler` validates it. On `OnConnectedAsync`, the hub adds the connection to `User_{UserId}` and `Session_{SessionId}` groups. The connection joins `Channel_{ChannelId}` groups on explicit `SubscribeToChannel` calls (the open `ChatWindow` subscribes; closing unsubscribes).

### 5.9 Multi-tab

Tabs share the cookie, so they share the session and land in the same `User_{UserId}` group. All tabs receive personal-scope events. Sign-out on one tab revokes only that browser's session; other browsers remain.

## 6. Presence, AFK, and real-time delivery

### 6.1 Presence state machine

Each tab reports its own state every 15 s via hub method `Heartbeat(state: 'active' | 'idle')`:

- `active`: user produced input in this tab in the last 15 s.
- `idle`: they did not.

Client-side activity detection listens to `pointerdown`, `keydown`, `visibilitychange`, `focus`; `setInterval` fires the heartbeat. Throttled to at most one immediate "active" heartbeat per 5 s.

Server stores tab state in a Redis hash per user:

```
HSET presence:user:{UserId} {ConnectionId} "{state}|{epochMs}"
EXPIRE presence:user:{UserId} 90
```

The hash's 90 s TTL cleans ghost presence if a tab crashes and `OnDisconnectedAsync` never fires.

Derived user presence (pure function of the hash):

- **online** — any field has `state=active AND epochMs > now − 30 s`.
- **afk** — all alive fields are `idle`, OR all fields are stale (60 s < age ≤ 90 s).
- **offline** — hash is empty (no alive tabs).

`OnDisconnectedAsync` removes that `ConnectionId`'s field; if the hash becomes empty, the user transitions to offline.

### 6.2 Presence broadcasting

A `PresenceHostedService` (1 s `PeriodicTimer`) scans a Redis `presence:dirty` set (populated on every heartbeat write) and recomputes derived state per user. On transition (per-user debounced against the previously emitted state), publishes `PresenceChanged(userId, state)` via the SignalR Redis backplane, fanning out to interested subscribers only: friends of the user plus members of shared channels. The 2 s spec target is met with room to spare.

### 6.3 Message send path

1. Client calls hub `SendMessage(channelId, clientMessageId, content, replyToId?, attachmentIds[])` with a client-generated UUID for `clientMessageId`.
2. Server authorizes (§8), inserts `Message`, updates any `Attachment` rows to set `MessageId`, commits.
3. Server broadcasts `MessageCreated(channelId, MessageDto)` to `Channel_{channelId}`.
4. Hub method returns `{ok: true, serverId, createdAt, clientMessageId}` to the caller; client replaces the optimistic "sending…" row. Echoed `MessageCreated` events are deduped by `clientMessageId`.
5. `SignalRHubOptions.MaximumReceiveMessageSize = 64 KB`. Attachments never traverse the hub.

### 6.4 Edit / delete

Hub methods `EditMessage(id, newContent)` and `DeleteMessage(id)` authorize, update, and broadcast `MessageEdited` / `MessageDeleted` to the channel group. Deleted messages render as "message deleted" (row preserved for thread integrity and audit).

### 6.5 Unread counters

On `MessageCreated`, for each channel member except the sender:

- `INCR unread:user:{userId}:channel:{channelId}` in Redis.
- Broadcast `UnreadChanged(channelId, newCount)` to `User_{userId}`.

On "open channel" the client calls `MarkRead(channelId, lastMessageId)`: updates `ChannelRead`, zeroes the Redis counter, broadcasts `UnreadChanged(channelId, 0)` so all that user's tabs sync.

### 6.6 Reliability (tech-nuances)

- `GlobalHubFilter` wraps all hub method invocations, catches exceptions, logs with correlation id, returns `{ok:false, code, message}` to the caller without dropping the socket.
- Client shows "sending…" until hub method returns `{ok:true}`. Timeout 8 s → "failed — retry" UI.
- On reconnect, for each open channel the client issues `GET /api/channels/{id}/messages?after={lastSeenId}` to catch up on anything missed.

### 6.7 Ban enforcement (instant)

When an admin bans a user from a channel:

1. `ChannelMember.BannedAt` is set and committed.
2. The hub finds all connections in `User_{bannedUserId}` currently subscribed to `Channel_{channelId}`, removes them from the group, and broadcasts `RemovedFromChannel(channelId, reason)`.
3. The client unsubscribes and closes the channel view.

Same mechanism applies when a user-to-user block is created, acting on the personal-dialog channel between them.

## 7. Attachments

### 7.1 Upload

`POST /api/attachments` — `multipart/form-data`, streamed via `System.IO.Pipelines` to disk (never buffered in memory). Constraints:

- Authenticated.
- `Content-Length` ≤ 20 MB for files, ≤ 3 MB when `Content-Type` starts with `image/`.
- Server sniffs magic bytes. `image/*` allowlist enforced for anything claimed as an image. Non-image "arbitrary file" stored as-is and always served with `Content-Disposition: attachment`.
- Server computes SHA-256 during streaming, then atomically renames to `/data/attachments/{yyyy}/{mm}/{dd}/{sha256}.bin`.
- Creates `Attachment` row with `UploaderId = current user`, `MessageId = null`.
- Returns `{attachmentId, originalFileName, contentType, sizeBytes}`.

### 7.2 Binding to a message

The hub `SendMessage` method receives `attachmentIds[]`. Server verifies each attachment belongs to the caller and has `MessageId IS NULL`, then sets `MessageId` in the same transaction as the message insert.

Orphaned attachments (no `MessageId` within 24 h) are swept by an `AttachmentSweeperService` (`HostedService`, hourly `PeriodicTimer`).

### 7.3 Download

`GET /api/attachments/{id}` — server looks up the attachment, resolves `MessageId → ChannelId`, authorizes the caller as a current non-banned `ChannelMember` of that channel, and streams from disk via `PhysicalFileResult`. `Content-Disposition: attachment; filename="{original}"` for files, `inline` for images.

Access is per-*current*-membership: a user who later loses access (left, was banned, private-room invitation revoked, room deleted) cannot download. The file persists on disk unless the room is deleted.

### 7.4 Deletion and ref counting

Because storage is content-addressable and dedupe is possible, file unlink is ref-counted: the disk file is deleted only when no remaining `Attachment` row references its `StoragePath`. A `StorageSweeperService` performs the unlink check after `Channel` / `Message` / `Attachment` deletions.

### 7.5 Paste and drop

Frontend `ChatInput` handles `onPaste` and `onDrop` by posting each file to `/api/attachments` and rendering pending thumbnails; the message sends once all uploads succeed.

## 8. Authorization, moderation, rate limiting

### 8.1 Authorization rules as a pure domain service

`IAuthorizationRules` in `Attic.Domain` exposes static-like rule methods, each returning a `Result` with a reason code:

| Action | Rule |
|---|---|
| Post to public/private room | `ChannelMember` exists and `BannedAt IS NULL`. |
| Post to personal dialog | `Friendship` exists AND no `UserBlock` in either direction. |
| Edit message | `SenderId = self` AND `DeletedAt IS NULL`. No time limit. |
| Delete message | `SenderId = self`, OR (room chat AND caller is admin/owner of that channel). Personal chats have no admins. |
| Delete room | Caller is `OwnerId`. |
| Leave room | Caller is a member AND not the owner. |
| Ban user from room | Caller is admin/owner; target is a member; target is not the owner. |
| Remove admin | Caller is admin/owner; target is not the owner. |
| Invite to private room | Caller is a current member of that room. |
| Send friend request | No existing friendship; no pending request in either direction; no `UserBlock` in either direction. |
| Block user | Always allowed. Side effects: delete `Friendship`, cancel pending `FriendRequest`s both ways, insert `UserBlock`, channel between them becomes read-only. |

Controllers and hub methods load the minimal data (`ChannelMember` row, block/friendship lookup) and then call the rule; they do not inline rule logic.

### 8.2 Moderation surfaces

- `GET /api/channels/{id}/bans` (admin) — uses `.IgnoreQueryFilters()` on `ChannelMember`; returns `BannedAt`, `BannedById`, reason.
- `DELETE /api/channels/{id}/bans/{userId}` — clears `BannedAt`/`BannedById`/`BanReason`. Re-joining a public room keeps them as `member` (admin role is not restored automatically).
- Admin-delete-message → soft-delete, broadcast `MessageDeleted`. Renders as "message deleted" for all viewers.
- `SaveChangesInterceptor` emits `AuditLog` rows on tracked actions. Not user-visible in the MVP; developer-queryable.

### 8.3 Rate limiting (built-in `RateLimiter`)

- Fixed window 5 / min per IP on `/api/auth/login`, `/api/auth/register`, `/api/auth/password/forgot`.
- Sliding window 60 / min per user on hub `SendMessage` and `POST /api/attachments`. Exceeded → hub returns `{ok:false, code:'rate_limited'}`.
- Concurrency limit 3 in-flight uploads per user.

### 8.4 Input validation and XSS

- `FluentValidation` validators on all request DTOs: email format, password min 8, username charset + length, content ≤ 3 072 bytes UTF-8.
- Message content stored as-is, rendered as plain text in the UI. React escapes by default; no HTML sanitization needed because nothing is rendered as HTML.

## 9. API surface

### 9.1 REST

All endpoints except `/api/auth/*` require the session cookie.

```
Auth
  POST   /api/auth/register                  { email, username, password }
  POST   /api/auth/login                     { email, password }
  POST   /api/auth/logout
  POST   /api/auth/change-password           { currentPassword, newPassword, signOutOtherSessions }
  POST   /api/auth/password/forgot           { email }
  POST   /api/auth/password/reset            { token, newPassword }
  POST   /api/auth/delete-account            { password }
  GET    /api/auth/me

Sessions
  GET    /api/sessions
  DELETE /api/sessions/{id}

Channels
  GET    /api/channels/public?search=&cursor=
  GET    /api/channels/mine
  POST   /api/channels                       { name, description, kind }
  GET    /api/channels/{id}
  PATCH  /api/channels/{id}                  { name?, description? }
  DELETE /api/channels/{id}
  POST   /api/channels/{id}/join
  POST   /api/channels/{id}/leave
  GET    /api/channels/{id}/members
  DELETE /api/channels/{id}/members/{userId}                               (admin — counts as a ban)
  POST   /api/channels/{id}/members/{userId}/role   { role }
  GET    /api/channels/{id}/bans                                           (admin)
  DELETE /api/channels/{id}/bans/{userId}                                  (admin)
  POST   /api/channels/{id}/invitations       { usernameOrId }
  POST   /api/invitations/{id}/accept
  POST   /api/invitations/{id}/decline
  GET    /api/invitations
  POST   /api/personal-chats/open             { username }

Messages
  GET    /api/channels/{id}/messages?before={cursorId}&limit=50            (infinite scroll, DESC)
  GET    /api/channels/{id}/messages?after={cursorId}&limit=200            (reconnect catch-up, ASC)
  POST   /api/channels/{id}/read              { lastMessageId }

Friends
  GET    /api/friends
  POST   /api/friend-requests                 { username, text? }
  GET    /api/friend-requests
  POST   /api/friend-requests/{id}/accept
  POST   /api/friend-requests/{id}/decline
  DELETE /api/friends/{userId}
  POST   /api/users/{userId}/block
  DELETE /api/users/{userId}/block
  GET    /api/users/search?q=

Attachments
  POST   /api/attachments                     (multipart, streamed)
  GET    /api/attachments/{id}                (auth-gated stream)
```

### 9.2 SignalR hub `/hub`

**Client → server methods:**

```
SendMessage(channelId, clientMessageId, content, replyToId?, attachmentIds[])  → { ok, serverId?, createdAt?, error? }
EditMessage(messageId, newContent)   → { ok, error? }
DeleteMessage(messageId)             → { ok, error? }
Heartbeat(state: 'active'|'idle')    → void
MarkRead(channelId, lastMessageId)   → void
SubscribeToChannel(channelId)        → { ok, error? }   # server authorizes membership before adding to Channel_{id}
UnsubscribeFromChannel(channelId)    → void
```

**Server → client events:**

```
MessageCreated(channelId, MessageDto)
MessageEdited(channelId, messageId, newContent, updatedAt)
MessageDeleted(channelId, messageId)
UnreadChanged(channelId, newCount)
PresenceChanged(userId, state)
ChannelMemberJoined(channelId, UserSummary)
ChannelMemberLeft(channelId, userId)
ChannelMemberRoleChanged(channelId, userId, role)
RemovedFromChannel(channelId, reason)
ChannelDeleted(channelId)
InvitationReceived(InvitationDto)
FriendRequestReceived(FriendRequestDto)
FriendRequestDecided(requestId, status)
FriendRemoved(userId)
Blocked(userId)
ForceLogout(sessionId)
```

### 9.3 Groups at the hub

- `User_{userId}` — every tab of this user.
- `Session_{sessionId}` — every tab of this user on this browser (used only for `ForceLogout`).
- `Channel_{channelId}` — connections that have explicitly `SubscribeToChannel`'d, so silent tabs do not receive traffic for channels they aren't viewing.

### 9.4 DTO contract

DTOs live in `Attic.Contracts`. An NSwag step at SPA build time generates `Attic.Contracts.ts` so the SPA compiles against the same types. Drift becomes a compile error.

## 10. Frontend integration

The React prototype is kept for visual components and design language, but its mocked in-component state is replaced with:

- **TanStack Query** for all REST data (catalog, message history, profile, friends, sessions). Keyset-paginated message history uses `useInfiniteQuery`.
- **SignalR client wrapper** (`@microsoft/signalr`) exposed via a React context + typed hooks. Event handlers reach into the TanStack Query cache via `queryClient.setQueryData` (append new messages, bump unread counts, update presence, remove banned members).
- **React Router** for navigation (login/register/forgot/reset, chat shell, contacts, sessions, profile).
- **Auth state** from `GET /api/auth/me` at app mount, exposed via `useAuth()`. A 401 from any query flips to the login screen.
- **Optimistic message send**: generate `clientMessageId`, show "sending" row, call hub, replace on ack / mark failed on error.
- **Smart scroll**: the existing `ChatWindow.tsx` behavior (lock to bottom unless user scrolled up) is preserved.

## 11. Observability

`Attic.ServiceDefaults` wires OpenTelemetry traces, metrics, and logs for ASP.NET Core, HttpClient, EF Core, SignalR, Redis, visible in the Aspire dashboard in dev and exportable via OTLP in prod.

Custom metrics via `System.Diagnostics.Metrics.Meter`:

- `attic.messages.sent` — counter, tags `channelKind`.
- `attic.presence.changes` — counter.
- `attic.attachments.bytes` — histogram.
- `attic.signalr.connections` — gauge.

Structured logging via `ILogger<T>`; `LoggerMessage` source generators for hot paths (hub methods, auth handler).

Health checks: `/health/live` and `/health/ready` (the latter gates on DB and Redis reachability).

## 12. Testing strategy

### 12.1 Unit tests (`Attic.Domain.Tests`, xUnit + Shouldly)

Every `IAuthorizationRules` method, every branch. AFK state machine (all-idle, mixed, stale fields, empty hash). Keyset cursor encode/decode. Personal-chat friendship + block gating. Password reset token lifecycle. Runs in milliseconds.

### 12.2 Integration tests (`Attic.Api.IntegrationTests`, Aspire + TestContainers + xUnit)

One `DistributedApplicationTestingBuilder` fixture brings up real Postgres + Redis + API. Tests hit real HTTP and a real hub client. Coverage by flow:

- Auth: register, duplicate email 409, login, wrong password 401, session list + revoke, change-password with "sign out others", password reset stub end-to-end.
- Channels: create, join, post, paginate 10 000 messages, catalog search, private invitation, admin ban triggers `RemovedFromChannel`.
- Personal: friend request → accept → send; block → channel frozen; unblock retains freeze until re-friended.
- Attachments: upload under/over limit, wrong content-type, bind via hub, download as member, download after ban 403.
- Realtime: two clients in a channel, send/receive within 3 s; multi-tab presence aggregation; session revoke fires `ForceLogout` to one browser only.

### 12.3 End-to-end tests (`Attic.Web.E2E`, Playwright)

Three golden-path scenarios against the running Aspire app:

- Register → create room → send message → reload → message persists.
- Two browser contexts: A invites B to a private room, B accepts, A sends, B sees in realtime.
- Upload image, recipient downloads it; banned user gets 403 on download.

### 12.4 CI

`dotnet test` (unit + integration with TestContainers via Docker-in-Docker) then `npx playwright test` after `dotnet run --project Attic.AppHost`. Playwright browsers cached by version for fast CI.

## 13. Delivery phases

Each phase ends with its integration and E2E tests green and can be demoed.

- **Phase 1 — Vertical slice.** Aspire AppHost + Postgres + Redis + `Attic.Api`; cookie auth with `Session` and unit-tested handler; register / login / logout / me; one hardcoded public channel; `SendMessage` + `MessageCreated` via hub; keyset pagination; React shell with auth + one chat view via TanStack Query + SignalR wrapper. Proof-of-plumbing integration test: register → login → post → read.
- **Phase 2 — Rooms.** Channel CRUD; public catalog with filtered indexes; `/api/channels/mine`; join/leave; private rooms; invitations; members list; owner/admin roles; room bans; admin-delete-message; `RemovedFromChannel` realtime. Sidebar with public/private tabs, room-details panel, create-room modal.
- **Phase 3 — Friends & personal chat.** Friend requests, accept/decline, remove; user-to-user block; personal-chat channel creation; channel freeze on block. Contacts view, friend-request modals.
- **Phase 4 — Messaging extras.** Attachments (upload/download/access control, orphan sweep, ref-counted unlink); paste + drop input; reply-to; edit + delete; "edited" indicator.
- **Phase 5 — Presence, sessions, notifications.** Heartbeats + Redis hash + `PresenceHostedService` + `PresenceChanged`; multi-tab client activity detection; `ChannelRead` + `UnreadChanged`; active-sessions screen + per-session revoke + `ForceLogout`; account deletion cascade.
- **Phase 6 — Hardening.** Rate limiting tuned; `GlobalHubFilter`; `AuditLog` writes finalized + optional admin read endpoint; security headers; HTTPS redirection; Kestrel limits; production Docker image; production compose.

## 14. Open items

- If moderation audit needs a user-visible UI (rather than DB-only), that's a Phase-6 addition not yet scoped.
- Image thumbnailing is deferred; revisit if client bandwidth or render latency becomes a problem.
- Horizontal scale beyond one API instance is out of scope but the Redis backplane keeps the door open.
