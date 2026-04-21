# Attic Phase 5 — Presence, Unread, Active Sessions, Account Deletion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship live presence (online / afk / offline via per-tab heartbeats and a periodic derivation loop), per-channel unread counters driven by `ChannelRead`, an active-sessions screen with per-session revoke via `ForceLogout`, and an account-deletion flow that transactionally tombstones the user and cascades owned content.

**Architecture:** A new `IPresenceStore` abstraction (Redis-backed in prod, in-memory for tests) stores per-connection tab state as `{userId, connectionId, state: 'active'|'idle', at: epochMs}`. `ChatHub.Heartbeat` writes to the store; `OnDisconnectedAsync` removes the connection row; a `PresenceHostedService` with a 1-second `PeriodicTimer` scans a dirty set, computes each user's derived state (`online`/`afk`/`offline`), and broadcasts `PresenceChanged(userId, state)` only to interested subscribers — friends of that user, plus members of channels they share. A new `ChannelRead` aggregate tracks `{channelId, userId, lastReadMessageId, updatedAt}`; `ChatHub.MarkRead` upserts it and broadcasts `UnreadChanged(channelId, 0)` on the user's group; each `SendMessage` broadcast step also fires `UnreadChanged(channelId, newCount)` to every member except the sender. `GET /api/sessions` lists active `Session` rows; `DELETE /api/sessions/{id}` revokes one and broadcasts `ForceLogout(sessionId)` on the `Session_{id}` SignalR group. `POST /api/auth/delete-account` soft-deletes the `User` (tombstoning email/username), hard-deletes owned `Channel` aggregates (with cascade), deletes the user's `ChannelMember` / `Friendship` / `FriendRequest` / `UserBlock` / `Session` / `ChannelRead` rows in a single transaction; the user's messages in *other* users' channels and in personal chats remain — they resolve as "deleted user" via the FK.

**Tech Stack:** Same as Phase 4 — .NET 10, Aspire 13.2.2, EF Core 10.0.5 + Npgsql, SignalR + Redis backplane, TanStack Query v5 + React Router v6 + `@microsoft/signalr` v8 + Tailwind 4. New dep: Aspire already wires `IConnectionMultiplexer` for `StackExchange.Redis` via `AddRedisClient("redis")`. The Redis presence store uses that directly.

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — presence + unread in §6.1-6.5; active sessions in §5.4; account deletion in §5.7; SignalR events (`PresenceChanged`, `UnreadChanged`, `ForceLogout`) in §9.2.

---

## Prerequisites — invariants from Phases 1-4

Do not regress any of these:

- **DbContext registration** uses `AddDbContext<AtticDbContext>` + `EnrichNpgsqlDbContext<AtticDbContext>()`.
- **Hub methods** read user id via `Context.User`; `CurrentUser` scoped service is HTTP-only.
- **Raw SQL in EF configs** uses snake_case identifiers unquoted.
- **`TimestampInterceptor`** respects `IsModified`; domain methods that own `UpdatedAt` set it explicitly (e.g. `Message.Edit`, `Channel.Rename`). Phase 5's new `ChannelRead.MarkRead` domain method owns its own `UpdatedAt` write.
- **Entity `UpdatedAt` properties** are `{ get; private set; }`.
- **Authorization rules** are pure static functions in `Attic.Domain.Services.AuthorizationRules`.
- **REST broadcasts** go through `*EventBroadcaster` scoped services, not direct `IHubContext` use in endpoints.
- **FluentValidation** validators auto-registered via `AddValidatorsFromAssemblyContaining<RegisterRequestValidator>`.
- **Aspire** is 13.2.2. Confirm `IConnectionMultiplexer` is already wired by `AddRedisClient("redis")` in `Program.cs` (Phase 1 should have done this for the SignalR Redis backplane; if not, add it).

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-5` (branched from merged `main` after Phase 4).
- `dotnet test tests/Attic.Domain.Tests` → 110 passing.
- `dotnet test tests/Attic.Api.IntegrationTests` → 54 passing.
- `cd src/Attic.Web && npm run lint && npm run build` exits 0.
- Aspire AppHost provides Redis via `AddRedis("redis")`; API project has `builder.AddRedisClient("redis")` in `Program.cs` registering `IConnectionMultiplexer`.
- Podman running with `DOCKER_HOST` set.

---

## File structure additions

```
src/
├── Attic.Domain/
│   └── Entities/
│       └── ChannelRead.cs                                     (new)
├── Attic.Infrastructure/
│   ├── Persistence/
│   │   ├── AtticDbContext.cs                                  (modify — DbSet<ChannelRead>)
│   │   ├── Configurations/
│   │   │   └── ChannelReadConfiguration.cs                    (new)
│   │   └── Migrations/
│   │       └── XXXXXXXXXXXXXX_AddChannelRead.cs               (generated)
│   └── Presence/
│       ├── IPresenceStore.cs                                  (new)
│       ├── PresenceState.cs                                   (new — enum + DTOs)
│       ├── RedisPresenceStore.cs                              (new)
│       └── InMemoryPresenceStore.cs                           (new — tests)
├── Attic.Contracts/
│   ├── Presence/
│   │   └── PresenceStateCode.cs                               (new — string consts online/afk/offline)
│   ├── Sessions/
│   │   └── ActiveSessionDto.cs                                (new)
│   └── Auth/
│       └── DeleteAccountRequest.cs                            (new)
├── Attic.Api/
│   ├── Endpoints/
│   │   ├── SessionsEndpoints.cs                               (new — GET + DELETE)
│   │   └── AuthEndpoints.cs                                   (modify — POST /api/auth/delete-account)
│   ├── Hubs/
│   │   ├── ChatHub.cs                                         (modify — Heartbeat, MarkRead, OnDisconnected)
│   │   ├── PresenceEventBroadcaster.cs                        (new)
│   │   └── SessionsEventBroadcaster.cs                        (new — ForceLogout)
│   ├── Services/
│   │   └── PresenceHostedService.cs                           (new)
│   ├── Validators/
│   │   ├── HeartbeatRequestValidator.cs                       (new — guards state enum)
│   │   └── DeleteAccountRequestValidator.cs                   (new)
│   └── Program.cs                                             (modify — register IPresenceStore + hosted service + endpoints)
└── Attic.Web/
    └── src/
        ├── api/
        │   ├── sessions.ts                                    (new)
        │   ├── authExtras.ts                                  (new — deleteAccount)
        │   └── signalr.ts                                     (modify — onPresenceChanged, onUnreadChanged, onForceLogout, heartbeat, markRead)
        ├── auth/
        │   ├── Sessions.tsx                                   (new — /settings/sessions page)
        │   └── DeleteAccountModal.tsx                         (new)
        ├── chat/
        │   ├── ChatShell.tsx                                  (modify — mount activity detection + sessions nav)
        │   ├── ChatWindow.tsx                                 (modify — mark read on active channel)
        │   ├── Sidebar.tsx                                    (modify — unread counts wire-up + presence dots on personal chats)
        │   ├── useActivityTracker.ts                          (new — pointerdown/keydown/visibility listener)
        │   ├── usePresence.ts                                 (new — local cache of userId → state)
        │   ├── useUnreadCounts.ts                             (new — subscribes to UnreadChanged)
        │   └── useMarkRead.ts                                 (new)
        ├── App.tsx                                            (modify — /settings/sessions route)
        └── types.ts                                           (modify — PresenceState, ActiveSessionDto, DeleteAccountRequest)
tests/
├── Attic.Domain.Tests/
│   └── ChannelReadTests.cs                                    (new)
└── Attic.Api.IntegrationTests/
    ├── PresenceFlowTests.cs                                   (new)
    ├── UnreadFlowTests.cs                                     (new)
    ├── SessionsFlowTests.cs                                   (new)
    └── DeleteAccountFlowTests.cs                              (new)
```

Total: ~20 new files, ~12 modified files.

---

## Task ordering rationale

Bottom-up as before. Each numbered task is one commit.

Four checkpoints:

- **Checkpoint 1 — Domain + Presence store + Infra (Tasks 1-8):** `ChannelRead` entity (TDD), `ChannelReadConfiguration` + migration, `IPresenceStore` abstraction with `RedisPresenceStore` + `InMemoryPresenceStore`, presence DTOs, DI registration.
- **Checkpoint 2 — Hub + Hosted service (Tasks 9-15):** `ChatHub.Heartbeat`, `OnDisconnectedAsync`, `MarkRead`, unread broadcast in `SendMessage`, `PresenceHostedService` (1s tick), `PresenceEventBroadcaster`, fan-out (friends + shared channels), integration tests.
- **Checkpoint 3 — REST + Account deletion + Sessions + Unread in listings (Tasks 16-24):** `GET /api/sessions` + `DELETE /api/sessions/{id}` with `ForceLogout`, `POST /api/auth/delete-account` with transactional cascade, `/api/channels/mine` populates `unreadCount`, integration tests.
- **Checkpoint 4 — Frontend (Tasks 25-33):** types + API clients, SignalR extension, activity tracker, presence cache, unread subscription, mark-read on channel open, sidebar wire-up, sessions page, delete-account modal, end-to-end smoke.

---

## Task 1: `ChannelRead` entity (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/ChannelRead.cs`
- Create: `tests/Attic.Domain.Tests/ChannelReadTests.cs`

Composite PK `(ChannelId, UserId)`, single mutable field `LastReadMessageId`.

- [ ] **Step 1.1: Write failing tests**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelReadTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_zero_read_marker()
    {
        var c = ChannelRead.Create(
            channelId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            userId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            lastReadMessageId: 0,
            now: T0);
        c.ChannelId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        c.UserId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        c.LastReadMessageId.ShouldBe(0);
        c.UpdatedAt.ShouldBe(T0);
    }

    [Fact]
    public void MarkRead_advances_marker_and_stamps_updatedAt()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 0, T0);
        c.MarkRead(42, T0.AddMinutes(5));
        c.LastReadMessageId.ShouldBe(42);
        c.UpdatedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void MarkRead_is_monotonic()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 100, T0);
        c.MarkRead(50, T0.AddMinutes(5));
        // Rewind rejected; remains 100.
        c.LastReadMessageId.ShouldBe(100);
    }

    [Fact]
    public void MarkRead_idempotent_on_same_id()
    {
        var c = ChannelRead.Create(Guid.NewGuid(), Guid.NewGuid(), 100, T0);
        c.MarkRead(100, T0.AddMinutes(5));
        c.LastReadMessageId.ShouldBe(100);
        // UpdatedAt still advances (we refreshed the row).
        c.UpdatedAt.ShouldBe(T0.AddMinutes(5));
    }
}
```

- [ ] **Step 1.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "ChannelReadTests"
```

- [ ] **Step 1.3: Implement `src/Attic.Domain/Entities/ChannelRead.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class ChannelRead
{
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public long LastReadMessageId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChannelRead() { }

    public static ChannelRead Create(Guid channelId, Guid userId, long lastReadMessageId, DateTimeOffset now)
        => new()
        {
            ChannelId = channelId,
            UserId = userId,
            LastReadMessageId = lastReadMessageId,
            UpdatedAt = now
        };

    public void MarkRead(long messageId, DateTimeOffset at)
    {
        if (messageId > LastReadMessageId) LastReadMessageId = messageId;
        UpdatedAt = at;
    }
}
```

- [ ] **Step 1.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "ChannelReadTests"
```

Expected: 4 passing.

- [ ] **Step 1.5: Commit**

```bash
git add src/Attic.Domain/Entities/ChannelRead.cs tests/Attic.Domain.Tests/ChannelReadTests.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(domain): add ChannelRead entity with monotonic marker"
```

---

## Task 2: `ChannelReadConfiguration` + `DbSet<ChannelRead>` + migration

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/ChannelReadConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/*_AddChannelRead.cs`

- [ ] **Step 2.1: Write `ChannelReadConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelReadConfiguration : IEntityTypeConfiguration<ChannelRead>
{
    public void Configure(EntityTypeBuilder<ChannelRead> b)
    {
        b.ToTable("channel_reads");
        b.HasKey(c => new { c.ChannelId, c.UserId });

        // Lookup by user for "how many unread across all my channels?" — secondary index.
        b.HasIndex(c => c.UserId).HasDatabaseName("ix_channel_reads_user");
    }
}
```

- [ ] **Step 2.2: Add `DbSet<ChannelRead>` to `AtticDbContext.cs`**

Append after the last existing DbSet (likely `DbSet<Attachment>`):

```csharp
    public DbSet<ChannelRead> ChannelReads => Set<ChannelRead>();
```

- [ ] **Step 2.3: Generate migration**

```bash
dotnet tool run dotnet-ef migrations add AddChannelRead \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 2.4: Sanity-check + commit**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase5-channel-read.sql
grep -i "channel_reads" /tmp/phase5-channel-read.sql | head -10
```

Must show `CREATE TABLE channel_reads (channel_id uuid, user_id uuid, last_read_message_id bigint, updated_at timestamp with time zone, CONSTRAINT pk_channel_reads PRIMARY KEY (channel_id, user_id))` and `CREATE INDEX ix_channel_reads_user ON channel_reads (user_id);`.

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Configurations/ChannelReadConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs \
        src/Attic.Infrastructure/Persistence/Migrations \
        docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(infra): add ChannelRead EF Core configuration + migration"
```

---

## Task 3: Presence DTOs + `IPresenceStore` abstraction

**Files:**
- Create: `src/Attic.Infrastructure/Presence/PresenceState.cs`
- Create: `src/Attic.Infrastructure/Presence/IPresenceStore.cs`

The store exposes async operations over the tab-state map keyed by `(userId, connectionId)`. Derived-state computation happens in the host service — the store just provides raw reads.

- [ ] **Step 3.1: Write `PresenceState.cs`**

```csharp
namespace Attic.Infrastructure.Presence;

public enum TabState
{
    Active = 0,
    Idle = 1
}

public enum UserPresence
{
    Online = 0,
    Afk = 1,
    Offline = 2
}

public readonly record struct TabHeartbeat(string ConnectionId, TabState State, long EpochMs);
```

- [ ] **Step 3.2: Write `IPresenceStore.cs`**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Attic.Infrastructure.Presence;

public interface IPresenceStore
{
    /// <summary>Upsert a tab's state. Refreshes the per-user TTL.</summary>
    Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct);

    /// <summary>Remove a tab from the user's live set (called from OnDisconnectedAsync).</summary>
    Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct);

    /// <summary>Load every live tab state for the given user.</summary>
    Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Return and clear the set of userIds that have had heartbeats since the last call.
    /// Used by <c>PresenceHostedService</c> to limit recomputation scope.
    /// </summary>
    Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct);
}
```

- [ ] **Step 3.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Presence docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(infra): add IPresenceStore abstraction + state enums"
```

---

## Task 4: `RedisPresenceStore`

**Files:**
- Create: `src/Attic.Infrastructure/Presence/RedisPresenceStore.cs`

Uses `IConnectionMultiplexer` (wired by Aspire's `AddRedisClient`). Per-user Redis HASH keyed by connection id, plus a SET `presence:dirty` for the dirty-users accumulator.

- [ ] **Step 4.1: Write `RedisPresenceStore.cs`**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Attic.Infrastructure.Presence;

public sealed class RedisPresenceStore(IConnectionMultiplexer connection) : IPresenceStore
{
    private static readonly TimeSpan HashTtl = TimeSpan.FromSeconds(90);
    private const string DirtyKey = "presence:dirty";

    private static string HashKey(Guid userId) => $"presence:user:{userId:D}";

    public async Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var key = HashKey(userId);
        var encoded = $"{(int)state}|{nowEpochMs}";
        var tx = db.CreateTransaction();
        _ = tx.HashSetAsync(key, connectionId, encoded);
        _ = tx.KeyExpireAsync(key, HashTtl);
        _ = tx.SetAddAsync(DirtyKey, userId.ToString("D"));
        await tx.ExecuteAsync();
    }

    public async Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var key = HashKey(userId);
        await db.HashDeleteAsync(key, connectionId);
        await db.SetAddAsync(DirtyKey, userId.ToString("D"));
    }

    public async Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var entries = await db.HashGetAllAsync(HashKey(userId));
        var tabs = new List<TabHeartbeat>(entries.Length);
        foreach (var e in entries)
        {
            var parts = e.Value.ToString().Split('|');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var s)) continue;
            if (!long.TryParse(parts[1], out var at)) continue;
            tabs.Add(new TabHeartbeat(e.Name!, (TabState)s, at));
        }
        return tabs;
    }

    public async Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct)
    {
        var db = connection.GetDatabase();
        var members = await db.SetMembersAsync(DirtyKey);
        if (members.Length == 0) return Array.Empty<Guid>();
        await db.KeyDeleteAsync(DirtyKey);
        var result = new List<Guid>(members.Length);
        foreach (var m in members)
            if (Guid.TryParse(m.ToString(), out var id)) result.Add(id);
        return result;
    }
}
```

- [ ] **Step 4.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Presence/RedisPresenceStore.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(infra): RedisPresenceStore (hash per user + dirty set)"
```

---

## Task 5: `InMemoryPresenceStore` (for unit-level testing + dev fallback)

**Files:**
- Create: `src/Attic.Infrastructure/Presence/InMemoryPresenceStore.cs`

Thread-safe dictionary-backed store. Used when Redis isn't available or for isolated tests.

- [ ] **Step 5.1: Write the file**

```csharp
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Attic.Infrastructure.Presence;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, TabHeartbeat>> _tabs = new();
    private readonly ConcurrentDictionary<Guid, byte> _dirty = new();

    public Task WriteHeartbeatAsync(Guid userId, string connectionId, TabState state, long nowEpochMs, CancellationToken ct)
    {
        var map = _tabs.GetOrAdd(userId, _ => new ConcurrentDictionary<string, TabHeartbeat>());
        map[connectionId] = new TabHeartbeat(connectionId, state, nowEpochMs);
        _dirty[userId] = 0;
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken ct)
    {
        if (_tabs.TryGetValue(userId, out var map)) map.TryRemove(connectionId, out _);
        _dirty[userId] = 0;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TabHeartbeat>> ListAsync(Guid userId, CancellationToken ct)
    {
        IReadOnlyList<TabHeartbeat> result = _tabs.TryGetValue(userId, out var map)
            ? map.Values.ToArray()
            : Array.Empty<TabHeartbeat>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Guid>> DrainDirtyAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> result = _dirty.Keys.ToArray();
        _dirty.Clear();
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 5.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Presence/InMemoryPresenceStore.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(infra): InMemoryPresenceStore (test/dev fallback)"
```

---

## Task 6: DI registration — `IPresenceStore` + options

**Files:**
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 6.1: Register the store**

Near the existing Redis client registration (`builder.AddRedisClient("redis");`), add:

```csharp
builder.Services.AddSingleton<Attic.Infrastructure.Presence.IPresenceStore,
                              Attic.Infrastructure.Presence.RedisPresenceStore>();
```

If `AddRedisClient("redis")` isn't present yet (e.g. Phase 1 didn't wire it), add:

```csharp
builder.AddRedisClient("redis");
```

Verify by reading `Program.cs` before adding.

- [ ] **Step 6.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): register RedisPresenceStore"
```

---

## Task 7: Contracts — `PresenceStateCode`, `ActiveSessionDto`, `DeleteAccountRequest`

**Files:**
- Create: `src/Attic.Contracts/Presence/PresenceStateCode.cs`
- Create: `src/Attic.Contracts/Sessions/ActiveSessionDto.cs`
- Create: `src/Attic.Contracts/Auth/DeleteAccountRequest.cs`

- [ ] **Step 7.1: `PresenceStateCode.cs`**

```csharp
namespace Attic.Contracts.Presence;

public static class PresenceStateCode
{
    public const string Online = "online";
    public const string Afk = "afk";
    public const string Offline = "offline";
}
```

- [ ] **Step 7.2: `ActiveSessionDto.cs`**

```csharp
namespace Attic.Contracts.Sessions;

public sealed record ActiveSessionDto(
    Guid Id,
    string UserAgent,
    string? Ip,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);
```

- [ ] **Step 7.3: `DeleteAccountRequest.cs`**

```csharp
namespace Attic.Contracts.Auth;

public sealed record DeleteAccountRequest(string Password);
```

- [ ] **Step 7.4: Build + commit**

```bash
dotnet build src/Attic.Contracts
git add src/Attic.Contracts/Presence src/Attic.Contracts/Sessions src/Attic.Contracts/Auth/DeleteAccountRequest.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(contracts): add Phase 5 presence/session/delete-account DTOs"
```

---

## Task 8: Checkpoint 1 marker

- [ ] **Step 8.1: Full domain test run**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: 110 prior + 4 ChannelRead = 114 passing.

- [ ] **Step 8.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 5 Checkpoint 1 (domain + presence store + infra) green"
```

---

## Task 9: `ChatHub.Heartbeat` + `OnDisconnectedAsync`

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

Add the `Heartbeat` method and override `OnDisconnectedAsync`.

- [ ] **Step 9.1: Inject `IPresenceStore` into the hub constructor**

Existing Phase 4 signature:
```csharp
public sealed class ChatHub(
    AtticDbContext db, IClock clock,
    IValidator<SendMessageRequest> sendValidator,
    IValidator<EditMessageRequest> editValidator) : Hub
```

Extend:
```csharp
public sealed class ChatHub(
    AtticDbContext db, IClock clock,
    IValidator<SendMessageRequest> sendValidator,
    IValidator<EditMessageRequest> editValidator,
    Attic.Infrastructure.Presence.IPresenceStore presenceStore) : Hub
```

- [ ] **Step 9.2: Add `Heartbeat` method**

Insert after `EditMessage`:

```csharp
    public async Task Heartbeat(string state)
    {
        var userId = UserId;
        if (userId is null) return;

        var tabState = state == "active"
            ? Attic.Infrastructure.Presence.TabState.Active
            : Attic.Infrastructure.Presence.TabState.Idle;

        var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
        await presenceStore.WriteHeartbeatAsync(userId.Value, Context.ConnectionId, tabState, nowMs, Context.ConnectionAborted);
    }
```

- [ ] **Step 9.3: Override `OnConnectedAsync` + `OnDisconnectedAsync`**

Phase 1 already has these. Find them (they add/remove the connection from `User_{userId}` / `Session_{sessionId}` groups) and extend the disconnect path:

```csharp
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = UserId;
        if (userId is not null)
        {
            await presenceStore.RemoveConnectionAsync(userId.Value, Context.ConnectionId, Context.ConnectionAborted);
        }
        await base.OnDisconnectedAsync(exception);
    }
```

If Phase 1's existing `OnDisconnectedAsync` already has a body, **splice** the presence-store call into it rather than replacing — preserve the existing group removal / housekeeping.

- [ ] **Step 9.4: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): ChatHub.Heartbeat + OnDisconnected persist/clean presence"
```

---

## Task 10: `PresenceEventBroadcaster` + `PresenceHostedService`

**Files:**
- Create: `src/Attic.Api/Hubs/PresenceEventBroadcaster.cs`
- Create: `src/Attic.Api/Services/PresenceHostedService.cs`
- Modify: `src/Attic.Api/Program.cs`

The broadcaster fires `PresenceChanged` to the interested parties of a given user. The host service drives derivation and debouncing.

- [ ] **Step 10.1: Write `PresenceEventBroadcaster.cs`**

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class PresenceEventBroadcaster(IHubContext<ChatHub> hub)
{
    /// <summary>Fan-out is the caller's responsibility — this just posts to the listed user groups.</summary>
    public Task PresenceChanged(IReadOnlyList<Guid> recipients, Guid userId, string state)
    {
        if (recipients.Count == 0) return Task.CompletedTask;
        var groups = recipients.Select(id => GroupNames.User(id)).ToArray();
        return hub.Clients.Groups(groups).SendAsync("PresenceChanged", userId, state);
    }
}
```

- [ ] **Step 10.2: Write `PresenceHostedService.cs`**

```csharp
using Attic.Api.Hubs;
using Attic.Contracts.Presence;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Presence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class PresenceHostedService(
    IServiceScopeFactory scopeFactory,
    IPresenceStore store,
    IClock clock,
    ILogger<PresenceHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private const long OnlineMaxAgeMs = 30_000;
    private const long AliveMaxAgeMs = 90_000;

    private readonly Dictionary<Guid, string> _last = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                var dirty = await store.DrainDirtyAsync(stoppingToken);
                if (dirty.Count == 0) continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var broadcaster = scope.ServiceProvider.GetRequiredService<PresenceEventBroadcaster>();

                var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
                foreach (var userId in dirty)
                {
                    var tabs = await store.ListAsync(userId, stoppingToken);
                    var state = Derive(tabs, nowMs);
                    if (_last.TryGetValue(userId, out var prev) && prev == state) continue;
                    _last[userId] = state;

                    var recipients = await FindRecipientsAsync(db, userId, stoppingToken);
                    await broadcaster.PresenceChanged(recipients, userId, state);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Presence tick failed.");
            }
        }
    }

    private static string Derive(IReadOnlyList<TabHeartbeat> tabs, long nowMs)
    {
        if (tabs.Count == 0) return PresenceStateCode.Offline;
        var alive = tabs.Where(t => nowMs - t.EpochMs <= AliveMaxAgeMs).ToList();
        if (alive.Count == 0) return PresenceStateCode.Offline;
        if (alive.Any(t => t.State == TabState.Active && nowMs - t.EpochMs <= OnlineMaxAgeMs))
            return PresenceStateCode.Online;
        return PresenceStateCode.Afk;
    }

    private static async Task<IReadOnlyList<Guid>> FindRecipientsAsync(
        AtticDbContext db, Guid userId, CancellationToken ct)
    {
        // Friends of the user.
        var friendIds = await db.Friendships.AsNoTracking()
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId)
            .ToListAsync(ct);

        // Members of channels the user is in (excluding self and excluding banned).
        var sharedChannelIds = db.ChannelMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChannelId);

        var coMemberIds = await db.ChannelMembers.AsNoTracking()
            .Where(m => sharedChannelIds.Contains(m.ChannelId) && m.UserId != userId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        return friendIds.Concat(coMemberIds).Distinct().ToList();
    }
}
```

- [ ] **Step 10.3: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<Attic.Api.Hubs.PresenceEventBroadcaster>();
builder.Services.AddHostedService<Attic.Api.Services.PresenceHostedService>();
```

- [ ] **Step 10.4: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/PresenceEventBroadcaster.cs src/Attic.Api/Services/PresenceHostedService.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): PresenceEventBroadcaster + PresenceHostedService (1s tick, debounced)"
```

---

## Task 11: `ChatHub.MarkRead` + `UnreadChanged` on send

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

- [ ] **Step 11.1: Add `MarkRead` method**

Insert after `Heartbeat`:

```csharp
    public async Task<object> MarkRead(Guid channelId, long lastMessageId)
    {
        var userId = UserId;
        if (userId is null) return new { ok = false };

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

        await Clients.Group(GroupNames.User(userId.Value))
            .SendAsync("UnreadChanged", channelId, 0);

        return new { ok = true };
    }
```

- [ ] **Step 11.2: Extend `SendMessage` to broadcast `UnreadChanged` per member**

After the existing `MessageCreated` broadcast, append:

```csharp
        // Bump per-member unread counters (except the sender).
        var memberIds = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.ChannelId == request.ChannelId && m.UserId != userId.Value)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var memberId in memberIds)
        {
            var read = await db.ChannelReads.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ChannelId == request.ChannelId && r.UserId == memberId);
            var lastRead = read?.LastReadMessageId ?? 0;
            var unreadCount = await db.Messages.AsNoTracking()
                .CountAsync(m => m.ChannelId == request.ChannelId && m.Id > lastRead);

            await Clients.Group(GroupNames.User(memberId))
                .SendAsync("UnreadChanged", request.ChannelId, unreadCount);
        }
```

For a channel with many members this is N queries — acceptable for Phase 5's target of 300 concurrent users + max 1 000 members; the spec mentions Redis caching as a Phase-6 optimization.

- [ ] **Step 11.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): ChatHub.MarkRead + UnreadChanged fan-out in SendMessage"
```

---

## Task 12: `HeartbeatRequestValidator` (noop guard)

**Files:**
- Create: `src/Attic.Api/Validators/HeartbeatRequestValidator.cs`

Hub's `Heartbeat(string state)` takes a raw string. Add a guard in the hub body (cheaper than full FluentValidation for a single-arg method):

- [ ] **Step 12.1: Guard directly in the hub**

Back in `ChatHub.Heartbeat`, replace the first line after the `if (userId is null) return;` check with:

```csharp
        if (state != "active" && state != "idle") return;
```

Skip creating a separate validator file. Commit the inline change:

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "fix(api): ChatHub.Heartbeat rejects unknown state values"
```

(The validator file is skipped — the inline guard is sufficient for a single-argument hub method. Adjust plan expectations accordingly.)

---

## Task 13: `PresenceFlowTests` — heartbeat + derived state + fan-out

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/PresenceFlowTests.cs`

- [ ] **Step 13.1: Write the test file**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class PresenceFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email, HttpClientHandler Handler)> RegisterFresh(CancellationToken ct)
        => TestHelpers.RegisterFreshWithHandler(fx, ct);

    [Fact]
    public async Task Friend_receives_PresenceChanged_on_heartbeat()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _, aliceHandler) = await RegisterFresh(ct);
        var (bob, bobUsername, _, bobHandler) = await RegisterFresh(ct);

        // Make them friends.
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var aliceCookie = TestHelpers.GetCookieHeader(aliceHandler, fx);
        var bobCookie = TestHelpers.GetCookieHeader(bobHandler, fx);

        await using var aliceHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = aliceCookie).Build();
        await using var bobHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = bobCookie).Build();
        await aliceHub.StartAsync(ct);
        await bobHub.StartAsync(ct);

        var received = new TaskCompletionSource<(Guid, string)>();
        bobHub.On<Guid, string>("PresenceChanged", (uid, state) => received.TrySetResult((uid, state)));

        await aliceHub.InvokeAsync("Heartbeat", "active", ct);

        // PresenceHostedService runs on a 1s tick — allow up to 5s.
        var (userId, statusCode) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        statusCode.ShouldBe("online");
    }
}
```

**Note:** This test requires `TestHelpers.RegisterFreshWithHandler` to be available (added in Phase 4's Checkpoint 2). If not, skip the helper and build the handler inline.

- [ ] **Step 13.2: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "PresenceFlowTests"
git add tests/Attic.Api.IntegrationTests/PresenceFlowTests.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "test(api): presence heartbeat + PresenceChanged fan-out"
```

---

## Task 14: `UnreadFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/UnreadFlowTests.cs`

- [ ] **Step 14.1: Write tests**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class UnreadFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Member_receives_UnreadChanged_when_other_sends()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, ownerHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var (member, _, _, memberHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ur-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        (await member.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var memberCookie = TestHelpers.GetCookieHeader(memberHandler, fx);
        await using var memberHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = memberCookie).Build();
        await memberHub.StartAsync(ct);

        var received = new TaskCompletionSource<(Guid channelId, int count)>();
        memberHub.On<Guid, int>("UnreadChanged", (cid, c) => received.TrySetResult((cid, c)));

        var ownerCookie = TestHelpers.GetCookieHeader(ownerHandler, fx);
        await using var ownerHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = ownerCookie).Build();
        await ownerHub.StartAsync(ct);
        await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        await ownerHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hi", null, null), ct);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.channelId.ShouldBe(channel.Id);
        evt.count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MarkRead_zeros_unread_and_broadcasts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, ownerHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var (member, _, _, memberHandler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"mr-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        (await member.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var ownerCookie = TestHelpers.GetCookieHeader(ownerHandler, fx);
        await using var ownerHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = ownerCookie).Build();
        await ownerHub.StartAsync(ct);
        await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await ownerHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hi", null, null), ct);
        var messageId = send.ServerId!.Value;

        var memberCookie = TestHelpers.GetCookieHeader(memberHandler, fx);
        await using var memberHub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = memberCookie).Build();
        await memberHub.StartAsync(ct);

        var zeroed = new TaskCompletionSource<int>();
        memberHub.On<Guid, int>("UnreadChanged", (cid, c) => {
            if (cid == channel.Id && c == 0) zeroed.TrySetResult(c);
        });

        await memberHub.InvokeAsync<JsonElement>("MarkRead", channel.Id, messageId, ct);

        var c = await zeroed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        c.ShouldBe(0);
    }
}
```

- [ ] **Step 14.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "UnreadFlowTests"
git add tests/Attic.Api.IntegrationTests/UnreadFlowTests.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "test(api): unread counters + MarkRead broadcasting"
```

---

## Task 15: Checkpoint 2 marker

- [ ] **Step 15.1: Full run**

```bash
dotnet test
```

Expected: Domain 114 + Integration 54 prior + 1 Presence + 2 Unread = 57 integration = 171 total green.

- [ ] **Step 15.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 5 Checkpoint 2 (hub presence + unread) green"
```

---

## Task 16: `SessionsEndpoints` — list + revoke

**Files:**
- Create: `src/Attic.Api/Endpoints/SessionsEndpoints.cs`
- Create: `src/Attic.Api/Hubs/SessionsEventBroadcaster.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 16.1: Write `SessionsEventBroadcaster.cs`**

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class SessionsEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task ForceLogout(Guid sessionId) =>
        hub.Clients.Group(GroupNames.Session(sessionId)).SendAsync("ForceLogout", sessionId);
}
```

**Note:** `GroupNames.Session(Guid)` must exist on the Phase 1 helper class. If not, add:

```csharp
public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
```

Verify by reading `src/Attic.Api/Hubs/GroupNames.cs`.

- [ ] **Step 16.2: Write `SessionsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Api.Hubs;
using Attic.Contracts.Common;
using Attic.Contracts.Sessions;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/sessions").RequireAuthorization();

        group.MapGet("/", ListMine);
        group.MapDelete("/{id:guid}", Revoke);

        return routes;
    }

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        HttpContext http,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;
        var currentSessionId = currentUser.SessionIdOrNull;
        var now = clock.UtcNow;

        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new ActiveSessionDto(
                s.Id, s.UserAgent ?? "", s.Ip, s.CreatedAt, s.LastSeenAt,
                IsCurrent: s.Id == currentSessionId))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Revoke(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        SessionsEventBroadcaster events,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var session = await db.Sessions.AsTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null) return Results.NotFound();
        if (session.RevokedAt is not null) return Results.NoContent();

        session.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await events.ForceLogout(id);
        return Results.NoContent();
    }
}
```

**Notes:**
- `CurrentUser.SessionIdOrNull` is expected. Phase 1 probably exposed `UserIdOrThrow`; if `SessionIdOrNull` isn't there, add it to `CurrentUser` — a small accessor that returns the session id claim value.
- `Session.Revoke(DateTimeOffset)` is expected on the domain entity. Phase 1 may have this or may just have a setter; verify in `src/Attic.Domain/Entities/Session.cs`. If missing, add:

```csharp
    public void Revoke(DateTimeOffset at) { if (RevokedAt is null) RevokedAt = at; }
```

- [ ] **Step 16.3: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<Attic.Api.Hubs.SessionsEventBroadcaster>();
```

And after `app.MapAttachmentsEndpoints();` (or wherever the last endpoint group is mapped):

```csharp
app.MapSessionsEndpoints();
```

- [ ] **Step 16.4: OnConnected joins the `Session_{id}` group**

In `ChatHub.OnConnectedAsync`, verify that the connection is added to the `Session_{SessionId}` group. Phase 1 probably already did this — if not, add:

```csharp
        var sessionId = /* read the session id claim from Context.User */;
        if (sessionId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Session(sessionId.Value));
```

Read `ChatHub.OnConnectedAsync` before editing — if the User group is added but not the Session group, add just the Session group line.

- [ ] **Step 16.5: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/SessionsEndpoints.cs src/Attic.Api/Hubs/SessionsEventBroadcaster.cs src/Attic.Api/Hubs/ChatHub.cs src/Attic.Api/Hubs/GroupNames.cs src/Attic.Api/Auth/CurrentUser.cs src/Attic.Domain/Entities/Session.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): GET/DELETE /api/sessions with ForceLogout broadcast"
```

Stage only the files that actually changed — skip any not modified.

---

## Task 17: `SessionsFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/SessionsFlowTests.cs`

- [ ] **Step 17.1: Write**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Sessions;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class SessionsFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task List_sessions_returns_current()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var resp = await client.GetAsync("/api/sessions", ct);
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<List<ActiveSessionDto>>(ct))!;
        body.ShouldNotBeEmpty();
        body.ShouldContain(s => s.IsCurrent);
    }

    [Fact]
    public async Task Revoke_other_session_fires_ForceLogout_on_that_session_group()
    {
        var ct = TestContext.Current.CancellationToken;

        // Register once (first session).
        var (client1, _, email, handler1) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        // Log in as the same user in a second "browser" (second handler → second session).
        var handler2 = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client2 = new HttpClient(handler2) { BaseAddress = fx.ApiClient.BaseAddress };
        (await client2.PostAsJsonAsync("/api/auth/login",
            new Attic.Contracts.Auth.LoginRequest(email, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        // Find the second session's id via /api/sessions from client2.
        var list = (await (await client2.GetAsync("/api/sessions", ct))
            .Content.ReadFromJsonAsync<List<ActiveSessionDto>>(ct))!;
        var secondSessionId = list.First(s => s.IsCurrent).Id;

        var cookie2 = TestHelpers.GetCookieHeader(handler2, fx);
        await using var hub2 = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookie2).Build();
        await hub2.StartAsync(ct);

        var received = new TaskCompletionSource<Guid>();
        hub2.On<Guid>("ForceLogout", sid => received.TrySetResult(sid));

        // client1 revokes client2's session.
        var del = await client1.DeleteAsync($"/api/sessions/{secondSessionId:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.ShouldBe(secondSessionId);
    }
}
```

- [ ] **Step 17.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "SessionsFlowTests"
git add tests/Attic.Api.IntegrationTests/SessionsFlowTests.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "test(api): sessions list + revoke ForceLogout"
```

---

## Task 18: `DeleteAccountRequestValidator` + endpoint

**Files:**
- Create: `src/Attic.Api/Validators/DeleteAccountRequestValidator.cs`
- Modify: `src/Attic.Api/Endpoints/AuthEndpoints.cs`

- [ ] **Step 18.1: Write validator**

```csharp
using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(r => r.Password).NotEmpty().WithErrorCode("password_required");
    }
}
```

- [ ] **Step 18.2: Append endpoint to `AuthEndpoints.cs`**

Find the `MapAuthEndpoints` method and add:

```csharp
        group.MapPost("/delete-account", DeleteAccount);
```

Append the handler:

```csharp
    private static async Task<IResult> DeleteAccount(
        [FromBody] DeleteAccountRequest req,
        IValidator<DeleteAccountRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        CurrentUser currentUser,
        HttpContext http,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var userId = currentUser.UserIdOrThrow;
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        if (!hasher.Verify(req.Password, user.PasswordHash))
            return Results.BadRequest(new ApiError("invalid_password", "Password verification failed."));

        var now = clock.UtcNow;

        // Transactional cascade.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Owned channels → hard delete; cascade via DbContext Model relationships if configured,
        // otherwise delete dependents explicitly.
        var ownedChannelIds = await db.Channels.AsTracking()
            .Where(c => c.OwnerId == userId)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (ownedChannelIds.Count > 0)
        {
            await db.ChannelInvitations.Where(i => ownedChannelIds.Contains(i.ChannelId)).ExecuteDeleteAsync(ct);
            await db.ChannelMembers.IgnoreQueryFilters().Where(m => ownedChannelIds.Contains(m.ChannelId)).ExecuteDeleteAsync(ct);
            await db.ChannelReads.Where(r => ownedChannelIds.Contains(r.ChannelId)).ExecuteDeleteAsync(ct);
            await db.Attachments.Where(a => a.MessageId != null && db.Messages.Any(m => m.Id == a.MessageId && ownedChannelIds.Contains(m.ChannelId))).ExecuteDeleteAsync(ct);
            await db.Messages.IgnoreQueryFilters().Where(m => ownedChannelIds.Contains(m.ChannelId)).ExecuteDeleteAsync(ct);
            await db.Channels.Where(c => ownedChannelIds.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }

        // Delete non-owned memberships.
        await db.ChannelMembers.IgnoreQueryFilters().Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ChannelReads.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);

        // Friend graph.
        await db.Friendships.Where(f => f.UserAId == userId || f.UserBId == userId).ExecuteDeleteAsync(ct);
        await db.FriendRequests.Where(r => r.SenderId == userId || r.RecipientId == userId).ExecuteDeleteAsync(ct);
        await db.UserBlocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ExecuteDeleteAsync(ct);

        // Sessions.
        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);

        // Invitations sent by this user (cross-channel).
        await db.ChannelInvitations.Where(i => i.InviterId == userId || i.InviteeId == userId).ExecuteDeleteAsync(ct);

        // Soft-delete the user with tombstone rewrite.
        user.SoftDelete(now);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        // Clear the caller's cookie.
        http.Response.Cookies.Delete("attic.session");
        return Results.NoContent();
    }
```

**Notes:**
- `User.SoftDelete(DateTimeOffset)` is expected — the existing Phase 1 domain already has this (rewrites Email/Username to tombstones). Verify the method signature in `src/Attic.Domain/Entities/User.cs`.
- `IPasswordHasher` is the existing hashing adapter. Phase 1 wired it; verify the interface name / method signature.
- The cascade uses `ExecuteDeleteAsync` (EF Core 7+ feature) to avoid loading rows. This is much faster for bulk deletes.
- The attachment cleanup here only removes database rows — the on-disk files get collected by `StorageSweeperService` from Phase 4.

- [ ] **Step 18.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Validators/DeleteAccountRequestValidator.cs src/Attic.Api/Endpoints/AuthEndpoints.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): POST /api/auth/delete-account with transactional cascade"
```

---

## Task 19: `DeleteAccountFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/DeleteAccountFlowTests.cs`

- [ ] **Step 19.1: Write**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class DeleteAccountFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Delete_account_cascades_owned_channels_and_friendships()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, aliceEmail, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await alice.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"del-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (bob, bobUsername, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var fr = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{fr.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var del = await alice.PostAsJsonAsync("/api/auth/delete-account",
            new DeleteAccountRequest("hunter2pw"), ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Alice's channel is gone.
        var view = await bob.GetAsync($"/api/channels/{channel.Id:D}", ct);
        view.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Bob has no friends now.
        var bobFriends = (await (await bob.GetAsync("/api/friends", ct))
            .Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        bobFriends.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_rejects_wrong_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var del = await client.PostAsJsonAsync("/api/auth/delete-account",
            new DeleteAccountRequest("wrong-pw"), ct);
        del.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 19.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "DeleteAccountFlowTests"
git add tests/Attic.Api.IntegrationTests/DeleteAccountFlowTests.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "test(api): delete-account cascades owned channels + friendships"
```

---

## Task 20: `GET /api/channels/mine` populates `unreadCount`

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`

Phase 2's `GetMyChannels` returns `UnreadCount: 0`. Phase 5 computes it from `ChannelRead.LastReadMessageId`.

- [ ] **Step 20.1: Update `GetMyChannels`**

Find the block that builds `ChannelSummary` and replace the `UnreadCount: 0` literal. After the existing `rows`/`counts` queries, add a third query:

```csharp
        var readMap = await db.ChannelReads.AsNoTracking()
            .Where(r => r.UserId == userId)
            .ToDictionaryAsync(r => r.ChannelId, r => r.LastReadMessageId, ct);

        var unreadMap = new Dictionary<Guid, int>();
        foreach (var r in rows)
        {
            var lastRead = readMap.TryGetValue(r.Id, out var lr) ? lr : 0;
            unreadMap[r.Id] = await db.Messages.AsNoTracking()
                .CountAsync(m => m.ChannelId == r.Id && m.Id > lastRead, ct);
        }
```

Then in the final `Select` that builds `ChannelSummary`, use `unreadMap[r.Id]` instead of the literal `0`.

- [ ] **Step 20.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(api): /api/channels/mine populates unreadCount"
```

---

## Task 21-24: Checkpoint 3 marker + final sweep

- [ ] **Step 21.1: Full integration run**

```bash
dotnet test tests/Attic.Api.IntegrationTests
```

Expected: 54 prior + 1 Presence + 2 Unread + 2 Sessions + 2 DeleteAccount = 61 passing.

- [ ] **Step 21.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 5 Checkpoint 3 (REST — sessions + delete-account + unread listings) green"
```

---

## Task 25: Frontend types + API clients

**Files:**
- Modify: `src/Attic.Web/src/types.ts`
- Create: `src/Attic.Web/src/api/sessions.ts`
- Create: `src/Attic.Web/src/api/authExtras.ts`

- [ ] **Step 25.1: Append types**

```ts
export type PresenceState = 'online' | 'afk' | 'offline';

export interface ActiveSessionDto {
  id: string;
  userAgent: string;
  ip: string | null;
  createdAt: string;
  lastSeenAt: string;
  isCurrent: boolean;
}

export interface DeleteAccountRequest {
  password: string;
}
```

- [ ] **Step 25.2: `sessions.ts`**

```ts
import { api } from './client';
import type { ActiveSessionDto } from '../types';

export const sessionsApi = {
  listMine: () => api.get<ActiveSessionDto[]>('/api/sessions'),
  revoke: (id: string) => api.delete<void>(`/api/sessions/${id}`),
};
```

- [ ] **Step 25.3: `authExtras.ts`**

```ts
import { api } from './client';
import type { DeleteAccountRequest } from '../types';

export const authExtrasApi = {
  deleteAccount: (req: DeleteAccountRequest) => api.post<void>('/api/auth/delete-account', req),
};
```

- [ ] **Step 25.4: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/types.ts src/Attic.Web/src/api/sessions.ts src/Attic.Web/src/api/authExtras.ts docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): typed presence + sessions + delete-account API clients"
```

---

## Task 26: SignalR wrapper — heartbeat, markRead, presence/unread/force-logout events

**Files:**
- Modify: `src/Attic.Web/src/api/signalr.ts`

- [ ] **Step 26.1: Extend `HubClient` interface**

Update the top import:
```ts
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto, FriendRequestDto, EditMessageRequest, EditMessageResponse, PresenceState } from '../types';
```

Add to the interface:
```ts
  heartbeat(state: 'active' | 'idle'): Promise<void>;
  markRead(channelId: string, lastMessageId: number): Promise<void>;
  onPresenceChanged(cb: (userId: string, state: PresenceState) => void): () => void;
  onUnreadChanged(cb: (channelId: string, count: number) => void): () => void;
  onForceLogout(cb: (sessionId: string) => void): () => void;
```

- [ ] **Step 26.2: Extend factory**

Inside the returned singleton:

```ts
    async heartbeat(state) {
      if (connection.state !== signalR.HubConnectionState.Connected) return;
      await connection.invoke('Heartbeat', state);
    },
    async markRead(channelId, lastMessageId) {
      await ensureStarted();
      await connection.invoke('MarkRead', channelId, lastMessageId);
    },
    onPresenceChanged: (cb) => on<[string, PresenceState]>('PresenceChanged', cb),
    onUnreadChanged: (cb) => on<[string, number]>('UnreadChanged', cb),
    onForceLogout: (cb) => on<[string]>('ForceLogout', cb),
```

- [ ] **Step 26.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/api/signalr.ts docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): SignalR client handles presence, unread, force-logout"
```

---

## Task 27: `useActivityTracker` + `usePresence`

**Files:**
- Create: `src/Attic.Web/src/chat/useActivityTracker.ts`
- Create: `src/Attic.Web/src/chat/usePresence.ts`

- [ ] **Step 27.1: `useActivityTracker.ts`**

```ts
import { useEffect, useRef } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

export function useActivityTracker() {
  const lastActiveSent = useRef<number>(0);

  useEffect(() => {
    let interval: number | null = null;
    let lastActivity = Date.now();

    function onActivity() {
      lastActivity = Date.now();
      if (Date.now() - lastActiveSent.current > 5_000) {
        lastActiveSent.current = Date.now();
        void getOrCreateHubClient().heartbeat('active');
      }
    }

    function tick() {
      const hub = getOrCreateHubClient();
      const state = Date.now() - lastActivity < 15_000 ? 'active' : 'idle';
      if (state === 'active') lastActiveSent.current = Date.now();
      void hub.heartbeat(state);
    }

    window.addEventListener('pointerdown', onActivity);
    window.addEventListener('keydown', onActivity);
    window.addEventListener('focus', onActivity);
    document.addEventListener('visibilitychange', onActivity);

    interval = window.setInterval(tick, 15_000);
    tick();   // immediate.

    return () => {
      window.removeEventListener('pointerdown', onActivity);
      window.removeEventListener('keydown', onActivity);
      window.removeEventListener('focus', onActivity);
      document.removeEventListener('visibilitychange', onActivity);
      if (interval !== null) window.clearInterval(interval);
    };
  }, []);
}
```

- [ ] **Step 27.2: `usePresence.ts`**

Client-wide map of `userId → state` kept in TanStack Query.

```ts
import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { PresenceState } from '../types';

// Keyed by userId, values are the last-received PresenceState.
export function usePresence(userId?: string): PresenceState | 'unknown' {
  const qc = useQueryClient();

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onPresenceChanged((uid, state) => {
      qc.setQueryData(['presence', uid], state);
    });
    return () => { off(); };
  }, [qc]);

  const { data } = useQuery<PresenceState>({
    queryKey: ['presence', userId],
    queryFn: () => Promise.resolve<PresenceState>('offline'),
    enabled: !!userId,
    staleTime: Infinity,
    initialData: undefined,
  });
  return userId ? (data ?? 'unknown') : 'unknown';
}
```

- [ ] **Step 27.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/useActivityTracker.ts src/Attic.Web/src/chat/usePresence.ts docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): client activity tracker + presence cache"
```

---

## Task 28: `useMarkRead` + `useUnreadCounts` + `ChatWindow` wire-up

**Files:**
- Create: `src/Attic.Web/src/chat/useMarkRead.ts`
- Create: `src/Attic.Web/src/chat/useUnreadCounts.ts`
- Modify: `src/Attic.Web/src/chat/ChatWindow.tsx`
- Modify: `src/Attic.Web/src/chat/useChannelList.ts`

- [ ] **Step 28.1: `useMarkRead.ts`**

```ts
import { useEffect } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

/** Mark the latest message in a channel as read. Debounced so quick scrolling doesn't hammer the hub. */
export function useMarkRead(channelId: string, latestMessageId: number | undefined) {
  useEffect(() => {
    if (!latestMessageId || latestMessageId < 0) return;
    const timer = window.setTimeout(() => {
      void getOrCreateHubClient().markRead(channelId, latestMessageId);
    }, 500);
    return () => window.clearTimeout(timer);
  }, [channelId, latestMessageId]);
}
```

- [ ] **Step 28.2: `useUnreadCounts.ts`**

Apply `UnreadChanged` into the `['channels', 'mine']` cache.

```ts
import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { ChannelSummary } from '../types';

export function useUnreadCountsSubscription() {
  const qc = useQueryClient();
  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onUnreadChanged((channelId, count) => {
      qc.setQueryData<ChannelSummary[]>(['channels', 'mine'], prev =>
        prev?.map(c => c.id === channelId ? { ...c, unreadCount: count } : c) ?? prev);
    });
    return () => { off(); };
  }, [qc]);
}
```

- [ ] **Step 28.3: Mount subscription from `useChannelList.ts`**

Add a call to `useUnreadCountsSubscription()` inside `useChannelList` (before the `return query`):

```ts
import { useUnreadCountsSubscription } from './useUnreadCounts';

export function useChannelList() {
  // ...existing useQuery + useEffect...
  useUnreadCountsSubscription();
  return query;
}
```

- [ ] **Step 28.4: Wire `useMarkRead` in `ChatWindow`**

Inside `ChatWindowFor`, after `const { items, ... } = useChannelMessages(channelId);`, add:

```tsx
  const latestMessageId = items[0]?.id && items[0].id > 0 ? items[0].id : undefined;
  useMarkRead(channelId, latestMessageId);
```

Import at top:
```tsx
import { useMarkRead } from './useMarkRead';
```

- [ ] **Step 28.5: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/useMarkRead.ts src/Attic.Web/src/chat/useUnreadCounts.ts src/Attic.Web/src/chat/useChannelList.ts src/Attic.Web/src/chat/ChatWindow.tsx docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): mark-read on channel open + unread-count cache subscription"
```

---

## Task 29: Mount `useActivityTracker` in `ChatShell`

**Files:**
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`

- [ ] **Step 29.1: Mount the tracker**

Add an import:
```tsx
import { useActivityTracker } from './useActivityTracker';
```

Inside the component body, alongside `useRemovedFromChannel()`:
```tsx
  useActivityTracker();
```

- [ ] **Step 29.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/ChatShell.tsx docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): activity tracker mounted on authenticated shell"
```

---

## Task 30: `Sessions.tsx` page + `/settings/sessions` route

**Files:**
- Create: `src/Attic.Web/src/auth/Sessions.tsx`
- Modify: `src/Attic.Web/src/App.tsx`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx` (render Sessions page on that pathname)

- [ ] **Step 30.1: Write `Sessions.tsx`**

```tsx
import { useEffect } from 'react';
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { sessionsApi } from '../api/sessions';
import { getOrCreateHubClient, disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';

export function Sessions() {
  const qc = useQueryClient();
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({
    queryKey: ['sessions'] as const,
    queryFn: () => sessionsApi.listMine(),
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onForceLogout(() => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    });
    return () => { off(); };
  }, [navigate, setUser]);

  const revoke = useMutation({
    mutationFn: (id: string) => sessionsApi.revoke(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['sessions'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Active sessions</h1>
      {isLoading && <div className="text-slate-500">Loading…</div>}
      <ul className="divide-y bg-white rounded border">
        {(data ?? []).map(s => (
          <li key={s.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{s.userAgent || 'Unknown client'}</div>
              <div className="text-xs text-slate-500">
                {s.ip ?? '?'} · last seen {new Date(s.lastSeenAt).toLocaleString()}
                {s.isCurrent && <span className="ml-2 text-blue-600">(this tab)</span>}
              </div>
            </div>
            {!s.isCurrent && (
              <button onClick={() => revoke.mutate(s.id)}
                      className="px-3 py-1 text-sm text-red-600">
                Revoke
              </button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 30.2: Add route in `App.tsx`**

Inside `<Route element={<AuthGate />}>`:
```tsx
          <Route path="/settings/sessions" element={<ChatShell />} />
```

- [ ] **Step 30.3: Render `<Sessions />` in `ChatShell.tsx` when pathname matches**

Add import + render branch. Inside the existing `<main>`:
```tsx
          {pathname === '/settings/sessions' && <Sessions />}
```

And add `pathname !== '/settings/sessions'` to the existing catch-all chat-view guard.

Import:
```tsx
import { Sessions } from '../auth/Sessions';
```

- [ ] **Step 30.4: Add a sidebar link**

In `Sidebar.tsx`, extend the footer nav to include a Settings link:
```tsx
        <Link to="/settings/sessions" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Sessions
        </Link>
```

If the footer is getting crowded, use an icon. For Phase 5 MVP, a text link is fine.

- [ ] **Step 30.5: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/auth/Sessions.tsx src/Attic.Web/src/App.tsx src/Attic.Web/src/chat/ChatShell.tsx src/Attic.Web/src/chat/Sidebar.tsx docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): /settings/sessions page with revoke + ForceLogout navigation"
```

---

## Task 31: `DeleteAccountModal` + entry point in the shell header

**Files:**
- Create: `src/Attic.Web/src/auth/DeleteAccountModal.tsx`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`

- [ ] **Step 31.1: Write `DeleteAccountModal.tsx`**

```tsx
import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { authExtrasApi } from '../api/authExtras';
import { disposeHubClient } from '../api/signalr';
import { useAuth } from './useAuth';

export function DeleteAccountModal({ onClose }: { onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const { setUser } = useAuth();
  const navigate = useNavigate();

  const del = useMutation({
    mutationFn: () => authExtrasApi.deleteAccount({ password }),
    onSuccess: () => {
      disposeHubClient();
      setUser(null);
      navigate('/login', { replace: true });
    },
    onError: () => setError('Password verification failed.'),
  });

  return (
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold text-red-600">Delete account</h2>
        <p className="text-sm text-slate-600">
          This permanently deletes your account and cascades to rooms you own. This action cannot be undone.
        </p>
        <input type="password" autoComplete="current-password"
               className="w-full border rounded px-3 py-2" placeholder="Confirm password"
               value={password} onChange={e => setPassword(e.target.value)} />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => del.mutate()} disabled={!password || del.isPending}
                  className="px-3 py-1 text-sm bg-red-600 text-white rounded disabled:opacity-50">
            {del.isPending ? 'Deleting…' : 'Delete account'}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 31.2: Add a "Delete account" option to the shell header**

In `ChatShell.tsx`, alongside the existing "Sign out" button, add a "Delete account" link that opens the modal. Keep the UI minimal — a single `<button>` that toggles a local `deleteOpen` state.

```tsx
  const [deleteOpen, setDeleteOpen] = useState(false);
  // ...existing code...
  // In the header:
  <button onClick={() => setDeleteOpen(true)} className="ml-4 text-red-600">Delete account</button>
  // At end of return, after createOpen modal:
  {deleteOpen && <DeleteAccountModal onClose={() => setDeleteOpen(false)} />}
```

Import:
```tsx
import { DeleteAccountModal } from '../auth/DeleteAccountModal';
```

- [ ] **Step 31.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/auth/DeleteAccountModal.tsx src/Attic.Web/src/chat/ChatShell.tsx docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): DeleteAccountModal + header entry point"
```

---

## Task 32: Presence dot in `Sidebar` personal chats

**Files:**
- Modify: `src/Attic.Web/src/chat/Sidebar.tsx`

For each personal channel in the Personal tab, show a tiny colored dot reflecting the other member's presence state. We don't have the counterparty's userId in the sidebar list today — deriving it requires a second query. For Phase 5 MVP, we'll:

1. In the `Personal` tab row, call `usePresence(otherUserId)` — but the sidebar only has the channel id.

A pragmatic simplification: **only** decorate channels where we've already cached the counterparty's id from a prior RoomDetails visit. Or look up channel-members on render. For MVP, skip the presence dot in the sidebar entirely; the `RoomDetails` panel (Phase 2) can render a presence dot per member. That's where the presence indicator is most useful and where member ids are already loaded.

- [ ] **Step 32.1: Add presence dot to `RoomDetails` member rows**

Open `src/Attic.Web/src/chat/RoomDetails.tsx`. At the top of each `<li>` rendering a member, prepend a presence dot:

```tsx
import { usePresence } from './usePresence';

// Inside the render loop:
{members?.map(m => {
  const presence = usePresence(m.userId);  // NOTE: this breaks React rules of hooks — see below.
```

**Rules-of-hooks workaround:** `usePresence` cannot be called in a loop. Instead, render each member as its own component:

```tsx
function MemberRow({ m, userId, canManage, toggleRole, ban }: {
  m: ChannelMemberSummary;
  userId: string | undefined;
  canManage: boolean;
  toggleRole: ReturnType<typeof useMutation>;  // or its mutate fn
  ban: ReturnType<typeof useMutation>;
}) {
  const presence = usePresence(m.userId);
  const dotColor =
    presence === 'online' ? 'bg-green-500' :
    presence === 'afk' ? 'bg-yellow-500' : 'bg-slate-300';
  return (
    <li className="flex items-center justify-between">
      <span className="flex items-center gap-2">
        <span className={`inline-block w-2 h-2 rounded-full ${dotColor}`} />
        {m.username}
        <span className="ml-1 text-xs text-slate-400">{m.role}</span>
      </span>
      {/* ...existing admin controls... */}
    </li>
  );
}
```

In the parent `RoomDetails` component, replace `{members?.map(m => <li>...</li>)}` with `{members?.map(m => <MemberRow key={m.userId} m={m} ... />)}`.

Keep the existing admin controls inline inside `MemberRow`.

- [ ] **Step 32.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/RoomDetails.tsx docs/superpowers/plans/2026-04-21-phase5-presence-sessions.md
git commit -m "feat(web): presence dot on RoomDetails member rows"
```

---

## Task 33: Final smoke

- [ ] **Step 33.1: Full test run**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: all green. Domain 114 + Integration 61 = 175 total.

- [ ] **Step 33.2: Frontend**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 0 errors.

- [ ] **Step 33.3: Marker**

```bash
git commit --allow-empty -m "chore: Phase 5 end-to-end smoke green"
```

---

## Phase 5 completion checklist

- [x] `ChannelRead` entity (monotonic marker) + configuration + migration
- [x] `IPresenceStore` abstraction with `RedisPresenceStore` + `InMemoryPresenceStore`
- [x] `ChatHub.Heartbeat` + `OnDisconnectedAsync` persist/clean presence
- [x] `PresenceHostedService` (1s tick, debounced) computes derived state, broadcasts `PresenceChanged` to friends + co-members
- [x] `ChatHub.MarkRead` + `UnreadChanged` broadcast on send (per non-sender member)
- [x] `GET /api/sessions` + `DELETE /api/sessions/{id}` with `ForceLogout` to `Session_{id}` group
- [x] `POST /api/auth/delete-account` transactionally tombstones user + cascades owned channels + friend graph + sessions
- [x] `GET /api/channels/mine` populates `unreadCount`
- [x] Integration tests: presence fan-out, unread counter, sessions revoke, delete-account cascade
- [x] FE: activity tracker, presence cache, mark-read on channel open, unread badges via cache subscription, /settings/sessions page, DeleteAccountModal, presence dots in RoomDetails

## What is deferred to later phases

- **Rate limiting tuned** (sliding window on hub, concurrency cap on uploads) — Phase 6.
- **`GlobalHubFilter`** for uniform `{ok:false, code, message}` error shape — Phase 6.
- **`AuditLog` admin read surface** — Phase 6.
- **Security headers + HTTPS redirect + Kestrel limits + production Docker** — Phase 6.
- **Redis-backed unread count cache** (current implementation queries DB per member per send) — Phase 6.
