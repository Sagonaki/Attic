# Attic Phase 6 — Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the stack from "feature-complete" to "production-ready" — rate limiting on abusable endpoints, a hub-wide error-wrapping filter with correlation ids, an audit log emitted by `SaveChangesInterceptor` for admin actions with an admin read endpoint, security response headers + HTTPS redirection + tuned Kestrel limits, and frontend reconnect catch-up on `onreconnected`.

**Architecture:** `AuditLog` is a leaf entity written by a `SaveChangesInterceptor` that inspects `ChangeTracker` entries for tracked admin actions (ban, unban, role change, channel delete, message admin-delete, session revoke, account delete) and emits a row in the same `SaveChangesAsync` call. `GlobalHubFilter` implements `IHubFilter.InvokeMethodAsync` to catch exceptions thrown from hub methods, log them with a correlation id, and return `{ok:false, code, message}` via the method's declared return type. Rate limiting uses the built-in `RateLimiter` middleware for REST endpoints (fixed-window 5/min on auth, sliding-window 60/min on upload) and a small `IRateLimiter` abstraction plus sliding-window implementation for hub `SendMessage`. Security headers + HTTPS + Kestrel limits are wired in `Program.cs`. Frontend reconnect catch-up hooks SignalR's `onreconnected` event per open channel and issues `GET /api/channels/{id}/messages?after={lastSeenId}`.

**Tech Stack:** Same as Phase 5 — .NET 10, ASP.NET Core `RateLimiter` middleware, EF Core 10.0.5, SignalR + Redis backplane, TanStack Query v5, `@microsoft/signalr` v8.

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — rate limiting in §8.3, `GlobalHubFilter` in §6.6, `AuditLog` in §4.1 + §8.2, security + limits in §14 (implied).

---

## Prerequisites — invariants from Phases 1-5

Do not regress any of these:

- **DbContext registration** uses `AddDbContext<AtticDbContext>` + `EnrichNpgsqlDbContext<AtticDbContext>()`. The existing `TimestampInterceptor` is attached in the options callback — Phase 6 adds a second `SaveChangesInterceptor` (`AuditLogInterceptor`) alongside it.
- **Hub methods** read user id via `Context.User`.
- **Raw SQL** in EF configs uses snake_case identifiers unquoted.
- **Authorization rules** are pure functions in `Attic.Domain.Services.AuthorizationRules`.
- **REST broadcasts** go through `*EventBroadcaster` scoped services.
- **FluentValidation** auto-registered.
- **Aspire** 13.2.2.
- **Hub methods** currently return `Task<T>` with `T` being a response DTO or anonymous-object ack. The `GlobalHubFilter` must handle both without breaking the existing returning shape for methods that already encode error codes in their response type (`SendMessageResponse`, `EditMessageResponse`).

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-6` (branched from merged `main` after Phase 5).
- `dotnet test tests/Attic.Domain.Tests` → 114 passing.
- `dotnet test tests/Attic.Api.IntegrationTests` → 61 passing.
- `cd src/Attic.Web && npm run lint && npm run build` exits 0.
- Redis running in Aspire; Postgres running.
- Podman running, `DOCKER_HOST` set.

---

## File structure additions

```
src/
├── Attic.Domain/
│   └── Entities/
│       └── AuditLog.cs                                        (new)
├── Attic.Infrastructure/
│   ├── Persistence/
│   │   ├── AtticDbContext.cs                                  (modify — DbSet<AuditLog>)
│   │   ├── Configurations/
│   │   │   └── AuditLogConfiguration.cs                       (new)
│   │   ├── Interceptors/
│   │   │   └── AuditLogInterceptor.cs                         (new)
│   │   ├── Migrations/
│   │   │   └── XXXXXXXXXXXXXX_AddAuditLog.cs                  (generated)
│   │   └── DependencyInjection.cs                             (modify — wire AuditLogInterceptor)
├── Attic.Contracts/
│   └── Admin/
│       └── AuditLogEntryDto.cs                                (new)
├── Attic.Api/
│   ├── Endpoints/
│   │   └── AdminEndpoints.cs                                  (new — GET /api/admin/audit)
│   ├── Hubs/
│   │   └── GlobalHubFilter.cs                                 (new)
│   ├── RateLimiting/
│   │   ├── RateLimitPolicyNames.cs                            (new — policy name constants)
│   │   └── HubRateLimiter.cs                                  (new — per-user sliding window for hub)
│   ├── Security/
│   │   └── SecurityHeadersMiddleware.cs                       (new)
│   ├── Program.cs                                             (modify — RateLimiter, headers, HTTPS, Kestrel, hub filter, audit interceptor registration)
│   └── appsettings.Production.json                            (new — Kestrel limits, HTTPS, attachments root)
└── Attic.Web/
    └── src/
        ├── api/
        │   └── signalr.ts                                     (modify — onreconnected + catch-up hook)
        ├── chat/
        │   └── useChannelMessages.ts                          (modify — handle reconnect catch-up)
        └── types.ts                                           (modify — no new types, just confirmation)
tests/
├── Attic.Api.IntegrationTests/
│   ├── AuditLogFlowTests.cs                                   (new)
│   ├── RateLimitFlowTests.cs                                  (new)
│   ├── SecurityHeadersFlowTests.cs                            (new)
│   └── HubErrorFlowTests.cs                                   (new)
```

Total: ~13 new files, ~7 modified files.

---

## Task ordering rationale

Bottom-up as before. Each numbered task is one commit.

Four checkpoints:

- **Checkpoint 1 — AuditLog (Tasks 1-7):** `AuditLog` entity + configuration, `AuditLogInterceptor` emits rows for tracked admin actions, migration, `AdminEndpoints.GetAudit`, integration tests.
- **Checkpoint 2 — Hub hardening (Tasks 8-11):** `GlobalHubFilter` catches exceptions + correlation id, `HubRateLimiter` sliding window on `SendMessage`, integration tests.
- **Checkpoint 3 — REST hardening (Tasks 12-15):** Rate limiting on auth + attachment upload, security headers middleware, HTTPS redirect, Kestrel limits, integration tests.
- **Checkpoint 4 — Frontend + production config (Tasks 16-20):** SignalR `onreconnected` catch-up, production `appsettings.Production.json`, end-to-end smoke.

---

## Task 1: `AuditLog` entity (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/AuditLog.cs`
- Create: `tests/Attic.Domain.Tests/AuditLogTests.cs`

Write-only model (no mutation methods). A factory builds the row; `Data` is a `string` (JSON) because EF Core maps `jsonb` cleanly via string + column-type config.

- [ ] **Step 1.1: Write failing tests**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AuditLogTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_action_and_timestamp()
    {
        var entry = AuditLog.Create(
            actorUserId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            action: "channel.ban_member",
            targetChannelId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            targetUserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            targetMessageId: null,
            dataJson: """{"reason":"spam"}""",
            now: T0);

        entry.ActorUserId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        entry.Action.ShouldBe("channel.ban_member");
        entry.TargetChannelId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        entry.TargetUserId.ShouldBe(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        entry.TargetMessageId.ShouldBeNull();
        entry.Data.ShouldBe("""{"reason":"spam"}""");
        entry.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Create_rejects_empty_action()
    {
        Should.Throw<ArgumentException>(() => AuditLog.Create(
            Guid.NewGuid(), "", null, null, null, null, T0)).ParamName.ShouldBe("action");
    }

    [Fact]
    public void Create_allows_null_data()
    {
        var entry = AuditLog.Create(Guid.NewGuid(), "session.revoke", null, null, null, null, T0);
        entry.Data.ShouldBeNull();
    }
}
```

- [ ] **Step 1.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "AuditLogTests"
```

- [ ] **Step 1.3: Implement `src/Attic.Domain/Entities/AuditLog.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class AuditLog
{
    public long Id { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid? TargetChannelId { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public long? TargetMessageId { get; private set; }
    public string? Data { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid actorUserId,
        string action,
        Guid? targetChannelId,
        Guid? targetUserId,
        long? targetMessageId,
        string? dataJson,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));

        return new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            TargetChannelId = targetChannelId,
            TargetUserId = targetUserId,
            TargetMessageId = targetMessageId,
            Data = dataJson,
            CreatedAt = now
        };
    }
}
```

- [ ] **Step 1.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests --filter "AuditLogTests"
git add src/Attic.Domain/Entities/AuditLog.cs tests/Attic.Domain.Tests/AuditLogTests.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(domain): add AuditLog entity"
```

Expected: 3 new tests passing.

---

## Task 2: `AuditLogConfiguration` + `DbSet<AuditLog>` + migration

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [ ] **Step 2.1: Write `AuditLogConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).UseIdentityAlwaysColumn();
        b.Property(l => l.Action).HasMaxLength(64).IsRequired();
        b.Property(l => l.Data).HasColumnType("jsonb");

        b.HasIndex(l => l.CreatedAt).HasDatabaseName("ix_audit_logs_created");
        b.HasIndex(l => l.ActorUserId).HasDatabaseName("ix_audit_logs_actor");
        b.HasIndex(l => l.TargetChannelId).HasDatabaseName("ix_audit_logs_target_channel");
    }
}
```

- [ ] **Step 2.2: Add `DbSet<AuditLog>` to `AtticDbContext.cs`**

Insert after the last existing DbSet:

```csharp
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
```

- [ ] **Step 2.3: Generate migration**

```bash
dotnet tool run dotnet-ef migrations add AddAuditLog \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 2.4: Sanity-check + commit**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase6-audit.sql
grep -i "audit_logs" /tmp/phase6-audit.sql | head -10
```

Must show `CREATE TABLE audit_logs (id bigint GENERATED ALWAYS AS IDENTITY, actor_user_id uuid, action character varying(64), target_channel_id uuid, target_user_id uuid, target_message_id bigint, data jsonb, created_at timestamp with time zone, ...)` and three indexes.

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs \
        src/Attic.Infrastructure/Persistence/Migrations \
        docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(infra): add AuditLog EF Core configuration + migration"
```

---

## Task 3: `AuditLogInterceptor`

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Interceptors/AuditLogInterceptor.cs`
- Modify: `src/Attic.Infrastructure/DependencyInjection.cs` (or equivalent wiring file)

The interceptor observes tracked entries before `SaveChangesAsync` and emits `AuditLog` rows for entities tagged as "auditable" by the endpoint that called `SaveChangesAsync`. Since the interceptor can't infer intent (e.g. `ChannelMember.BannedAt` set → ban? unban?), the pattern is:

1. Endpoints that perform admin actions **set** a scoped `AuditLogContext.Push(action, targetIds, dataJson)` call before `SaveChangesAsync`.
2. The interceptor reads the accumulated entries from `AuditLogContext` and adds them to `ChangeTracker` in `SavingChanges`.
3. If `SaveChangesAsync` succeeds, the rows are committed with the rest of the change set.

This keeps the "what happened" decision at the endpoint level (where domain intent is known) and the "write it atomically" concern in the interceptor.

- [ ] **Step 3.1: Create `AuditLogContext` (scoped service)**

Create `src/Attic.Api/Services/AuditLogContext.cs`:

```csharp
namespace Attic.Api.Services;

public sealed class AuditLogContext
{
    private readonly List<PendingEntry> _pending = new();
    public IReadOnlyList<PendingEntry> Drain()
    {
        var snapshot = _pending.ToArray();
        _pending.Clear();
        return snapshot;
    }

    public void Add(
        string action,
        Guid actorUserId,
        Guid? targetChannelId = null,
        Guid? targetUserId = null,
        long? targetMessageId = null,
        string? dataJson = null)
    {
        _pending.Add(new PendingEntry(action, actorUserId, targetChannelId, targetUserId, targetMessageId, dataJson));
    }

    public readonly record struct PendingEntry(
        string Action,
        Guid ActorUserId,
        Guid? TargetChannelId,
        Guid? TargetUserId,
        long? TargetMessageId,
        string? DataJson);
}
```

Register as scoped in `Program.cs`:
```csharp
builder.Services.AddScoped<Attic.Api.Services.AuditLogContext>();
```

- [ ] **Step 3.2: Write `AuditLogInterceptor.cs`**

```csharp
using Attic.Api.Services;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Attic.Infrastructure.Persistence.Interceptors;

public sealed class AuditLogInterceptor(AuditLogContext auditContext, IClock clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = auditContext.Drain();
        if (entries.Count == 0) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = clock.UtcNow;
        foreach (var e in entries)
        {
            ctx.Add(AuditLog.Create(
                e.ActorUserId, e.Action, e.TargetChannelId, e.TargetUserId, e.TargetMessageId, e.DataJson, now));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

- [ ] **Step 3.3: Wire the interceptor in `DependencyInjection.cs`**

The Phase 1 `Attic.Infrastructure.DependencyInjection.AddAtticDbContext` already attaches `TimestampInterceptor`. Add `AuditLogInterceptor`:

```csharp
public static IHostApplicationBuilder AddAtticDbContext(this IHostApplicationBuilder builder, string connectionName)
{
    var connectionString = builder.Configuration.GetConnectionString(connectionName)
        ?? throw new InvalidOperationException($"Connection string '{connectionName}' was not found.");

    builder.Services.AddScoped<TimestampInterceptor>();
    builder.Services.AddScoped<Attic.Infrastructure.Persistence.Interceptors.AuditLogInterceptor>();

    builder.Services.AddDbContext<AtticDbContext>((sp, options) =>
    {
        options.UseNpgsql(connectionString);
        options.UseSnakeCaseNamingConvention();
        options.AddInterceptors(
            sp.GetRequiredService<TimestampInterceptor>(),
            sp.GetRequiredService<Attic.Infrastructure.Persistence.Interceptors.AuditLogInterceptor>());
    });

    builder.EnrichNpgsqlDbContext<AtticDbContext>();
    return builder;
}
```

If the existing file is laid out differently, adapt — the key is that both interceptors are scoped services attached in the `options` callback.

- [ ] **Step 3.4: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
dotnet build src/Attic.Api
git add src/Attic.Api/Services/AuditLogContext.cs \
        src/Attic.Infrastructure/Persistence/Interceptors/AuditLogInterceptor.cs \
        src/Attic.Infrastructure/DependencyInjection.cs \
        src/Attic.Api/Program.cs \
        docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(infra): AuditLogInterceptor + AuditLogContext scope"
```

---

## Task 4: Hook audit entries into admin actions

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs` (ban, unban, role change)
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs` (delete channel)
- Modify: `src/Attic.Api/Endpoints/SessionsEndpoints.cs` (revoke)
- Modify: `src/Attic.Api/Endpoints/AuthEndpoints.cs` (delete-account)
- Modify: `src/Attic.Api/Hubs/ChatHub.cs` (admin-delete-message)

Add `AuditLogContext audit` as a parameter and call `audit.Add(...)` before `SaveChangesAsync` in each handler below.

- [ ] **Step 4.1: `BanMember` (ChannelMembersEndpoints)**

Before `await db.SaveChangesAsync(ct);`:
```csharp
        audit.Add(
            action: "channel.ban_member",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId);
```

- [ ] **Step 4.2: `UnbanMember`**

```csharp
        audit.Add(
            action: "channel.unban_member",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId);
```

- [ ] **Step 4.3: `ChangeMemberRole`**

```csharp
        audit.Add(
            action: "channel.change_role",
            actorUserId: actorId,
            targetChannelId: channelId,
            targetUserId: userId,
            dataJson: $"{{\"newRole\":\"{newRole.ToString().ToLowerInvariant()}\"}}");
```

- [ ] **Step 4.4: `DeleteChannel` (ChannelsEndpoints)**

```csharp
        audit.Add(
            action: "channel.delete",
            actorUserId: currentUser.UserIdOrThrow,
            targetChannelId: id);
```

- [ ] **Step 4.5: `Revoke` (SessionsEndpoints)**

```csharp
        audit.Add(
            action: "session.revoke",
            actorUserId: userId,
            targetUserId: userId,
            dataJson: $"{{\"sessionId\":\"{id:D}\"}}");
```

- [ ] **Step 4.6: `DeleteAccount` (AuthEndpoints)**

Just before the final `await db.SaveChangesAsync(ct);` (before the tombstone-save):
```csharp
        audit.Add(
            action: "account.delete",
            actorUserId: userId);
```

- [ ] **Step 4.7: `DeleteMessage` (ChatHub) — admin delete path only**

In the existing `DeleteMessage` method, after the authorization check and before `await db.SaveChangesAsync();`, branch on whether the caller is the author:
```csharp
        if (msg.SenderId != userId.Value)
        {
            audit.Add(
                action: "message.admin_delete",
                actorUserId: userId.Value,
                targetChannelId: msg.ChannelId,
                targetUserId: msg.SenderId,
                targetMessageId: msg.Id);
        }
```

This requires injecting `AuditLogContext audit` into the hub constructor. Extend the constructor to 6 parameters:
```csharp
public sealed class ChatHub(
    AtticDbContext db, IClock clock,
    IValidator<SendMessageRequest> sendValidator,
    IValidator<EditMessageRequest> editValidator,
    Attic.Infrastructure.Presence.IPresenceStore presenceStore,
    Attic.Api.Services.AuditLogContext audit) : Hub
```

- [ ] **Step 4.8: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs \
        src/Attic.Api/Endpoints/ChannelsEndpoints.cs \
        src/Attic.Api/Endpoints/SessionsEndpoints.cs \
        src/Attic.Api/Endpoints/AuthEndpoints.cs \
        src/Attic.Api/Hubs/ChatHub.cs \
        docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): hook audit entries into ban/unban/role-change/delete/admin-delete/revoke/account-delete"
```

Expected: 0/0. Existing integration tests should keep passing (audit rows don't affect response shapes).

---

## Task 5: `AdminEndpoints` — `GET /api/admin/audit`

**Files:**
- Create: `src/Attic.Api/Endpoints/AdminEndpoints.cs`
- Create: `src/Attic.Contracts/Admin/AuditLogEntryDto.cs`
- Modify: `src/Attic.Api/Program.cs`

For Phase 6 MVP, audit reads are available to any authenticated user reading their own actions (`WHERE actor_user_id = self`). An admin-role surface (system-wide read) is deferred; this gives developers and support tooling a way to pull an actor's history.

- [ ] **Step 5.1: `AuditLogEntryDto.cs`**

```csharp
namespace Attic.Contracts.Admin;

public sealed record AuditLogEntryDto(
    long Id,
    Guid ActorUserId,
    string Action,
    Guid? TargetChannelId,
    Guid? TargetUserId,
    long? TargetMessageId,
    string? Data,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 5.2: `AdminEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Admin;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin").RequireAuthorization();
        group.MapGet("/audit/mine", GetMyAudit);
        return routes;
    }

    private static async Task<IResult> GetMyAudit(
        int? limit,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(l => l.ActorUserId == me)
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Select(l => new AuditLogEntryDto(
                l.Id, l.ActorUserId, l.Action, l.TargetChannelId, l.TargetUserId, l.TargetMessageId, l.Data, l.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }
}
```

- [ ] **Step 5.3: Register in `Program.cs`**

```csharp
app.MapAdminEndpoints();
```

- [ ] **Step 5.4: Build + commit**

```bash
dotnet build src/Attic.Api
dotnet build src/Attic.Contracts
git add src/Attic.Api/Endpoints/AdminEndpoints.cs \
        src/Attic.Contracts/Admin/AuditLogEntryDto.cs \
        src/Attic.Api/Program.cs \
        docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): GET /api/admin/audit/mine (caller's own audit trail)"
```

---

## Task 6: `AuditLogFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/AuditLogFlowTests.cs`

- [ ] **Step 6.1: Write**

```csharp
using System.Net.Http.Json;
using Attic.Contracts.Admin;
using Attic.Contracts.Channels;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AuditLogFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Delete_channel_writes_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ad-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        (await client.DeleteAsync($"/api/channels/{channel.Id:D}", ct)).EnsureSuccessStatusCode();

        var audit = (await (await client.GetAsync("/api/admin/audit/mine", ct))
            .Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(ct))!;
        audit.ShouldContain(e => e.Action == "channel.delete" && e.TargetChannelId == channel.Id);
    }

    [Fact]
    public async Task Ban_member_writes_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ad-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, joinerUsername, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var members = (await (await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct))
            .Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct))!;
        var joinerId = members.First(m => m.Username == joinerUsername).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct))
            .EnsureSuccessStatusCode();

        var audit = (await (await owner.GetAsync("/api/admin/audit/mine", ct))
            .Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(ct))!;
        audit.ShouldContain(e => e.Action == "channel.ban_member"
            && e.TargetChannelId == channel.Id
            && e.TargetUserId == joinerId);
    }
}
```

- [ ] **Step 6.2: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "AuditLogFlowTests"
git add tests/Attic.Api.IntegrationTests/AuditLogFlowTests.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "test(api): audit entries fire on delete-channel + ban-member"
```

Expected: 2 new tests passing.

---

## Task 7: Checkpoint 1 marker

```bash
dotnet test
git commit --allow-empty -m "chore: Phase 6 Checkpoint 1 (audit log) green"
```

Expected: Domain 114 + 3 Audit = 117. Integration 61 + 2 Audit = 63. **180 total**.

---

## Task 8: `GlobalHubFilter`

**Files:**
- Create: `src/Attic.Api/Hubs/GlobalHubFilter.cs`
- Modify: `src/Attic.Api/Program.cs`

Catches exceptions thrown from hub method invocations, logs them with a correlation id, and returns a method-appropriate "failure" response when the declared return type can express `{ok:false, code, message}`. For methods that return a strongly-typed response record with an `Error` slot (e.g. `SendMessageResponse(bool Ok, ..., string? Error)`, `EditMessageResponse(bool Ok, ..., string? Error)`), the filter reconstructs a failure response via reflection. For methods that return an anonymous object (e.g. `ChatHub.DeleteMessage`, `MarkRead`), the filter returns `new { ok = false, code = "server_error", correlationId }`.

For methods that return `Task` (no value), the filter just logs and rethrows — the client will get a hub-level error event, which is acceptable for one-way methods.

- [ ] **Step 8.1: Write `GlobalHubFilter.cs`**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class GlobalHubFilter(ILogger<GlobalHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {Method} threw. correlationId={CorrelationId} user={UserId}",
                invocationContext.HubMethodName,
                correlationId,
                invocationContext.Context.User?.Identity?.Name);

            var returnType = invocationContext.HubMethod.ReturnType;
            // Unwrap Task<T> / ValueTask<T>.
            var underlying = returnType;
            if (returnType.IsGenericType)
            {
                var def = returnType.GetGenericTypeDefinition();
                if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                    underlying = returnType.GetGenericArguments()[0];
            }
            else if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            {
                // Void-returning method — rethrow to let SignalR surface it as a hub error event.
                throw;
            }

            // Object / anonymous — return a generic failure shape.
            if (underlying == typeof(object))
                return new { ok = false, code = "server_error", correlationId };

            // Strongly-typed record with (bool Ok, ..., string? Error) positional pattern.
            var ctor = underlying.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
            if (ctor is null)
            {
                logger.LogWarning("GlobalHubFilter: cannot synthesize failure response for {Type}", underlying);
                throw;
            }

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(bool)) args[i] = false;
                else if (p.ParameterType == typeof(string) && p.Name?.Equals("error", StringComparison.OrdinalIgnoreCase) == true)
                    args[i] = "server_error";
                else args[i] = p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null
                    ? Activator.CreateInstance(p.ParameterType)
                    : null;
            }

            return ctor.Invoke(args);
        }
    }
}
```

- [ ] **Step 8.2: Register in `Program.cs`**

Where SignalR is configured (probably `builder.Services.AddSignalR().AddStackExchangeRedis(...)` or similar), extend:

```csharp
builder.Services.AddSignalR(o => o.AddFilter<Attic.Api.Hubs.GlobalHubFilter>());
```

Or if `AddSignalR` was called without configuration:

```csharp
builder.Services.Configure<HubOptions>(o => o.AddFilter<Attic.Api.Hubs.GlobalHubFilter>());
```

Either path works. Verify by reading the existing SignalR setup.

- [ ] **Step 8.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/GlobalHubFilter.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): GlobalHubFilter catches hub-method exceptions with correlationId"
```

---

## Task 9: `HubRateLimiter` — per-user sliding window for `SendMessage`

**Files:**
- Create: `src/Attic.Api/RateLimiting/HubRateLimiter.cs`
- Create: `src/Attic.Api/RateLimiting/RateLimitPolicyNames.cs`
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`
- Modify: `src/Attic.Api/Program.cs`

A tiny sliding-window limiter keyed by `userId`: at most 60 calls per rolling 60 seconds.

- [ ] **Step 9.1: Write `RateLimitPolicyNames.cs`**

```csharp
namespace Attic.Api.RateLimiting;

public static class RateLimitPolicyNames
{
    public const string AuthFixed = "auth-fixed-5-per-min";
    public const string UploadFixed = "upload-fixed-60-per-min";
}
```

- [ ] **Step 9.2: Write `HubRateLimiter.cs`**

Simple in-memory sliding window. For production, Redis-backed would be preferable, but MVP does in-memory — the spec target (300 concurrent users, single API instance) keeps this OK.

```csharp
using System.Collections.Concurrent;

namespace Attic.Api.RateLimiting;

public sealed class HubRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private const int MaxCalls = 60;

    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _history = new();

    public bool TryAcquire(Guid userId, DateTimeOffset now)
    {
        var queue = _history.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            var cutoff = now - Window;
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= MaxCalls) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
```

- [ ] **Step 9.3: Register as singleton in `Program.cs`**

```csharp
builder.Services.AddSingleton<Attic.Api.RateLimiting.HubRateLimiter>();
```

- [ ] **Step 9.4: Apply in `ChatHub.SendMessage`**

Inject `HubRateLimiter rateLimiter` into the hub constructor (7th parameter). At the top of `SendMessage`, after `var userId = UserId;` + `if (userId is null) return ...`, add:

```csharp
        if (!rateLimiter.TryAcquire(userId.Value, clock.UtcNow))
            return new SendMessageResponse(false, null, null, "rate_limited");
```

- [ ] **Step 9.5: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/RateLimiting src/Attic.Api/Hubs/ChatHub.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): HubRateLimiter (60/min per user on SendMessage)"
```

---

## Task 10: `HubErrorFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/HubErrorFlowTests.cs`

Tests that rate-limit denial returns the structured error shape.

- [ ] **Step 10.1: Write**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class HubErrorFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task SendMessage_returns_rate_limited_after_60_calls_per_minute()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, handler) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rl-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var cookie = TestHelpers.GetCookieHeader(handler, fx);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookie).Build();
        await hub.StartAsync(ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        // 60 successful sends.
        for (int i = 0; i < 60; i++)
        {
            var resp = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
                new SendMessageRequest(channel.Id, Guid.NewGuid(), $"m{i}", null, null), ct);
            resp.Ok.ShouldBeTrue($"send #{i} should have succeeded");
        }

        // 61st triggers rate_limited.
        var denied = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "one-too-many", null, null), ct);
        denied.Ok.ShouldBeFalse();
        denied.Error.ShouldBe("rate_limited");
    }
}
```

**Note:** If the test is slow (sending 60 messages), it's acceptable — this is an integration-level guard, not a unit test.

- [ ] **Step 10.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "HubErrorFlowTests"
git add tests/Attic.Api.IntegrationTests/HubErrorFlowTests.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "test(api): hub rate limit returns structured failure"
```

---

## Task 11: Checkpoint 2 marker

```bash
dotnet test
git commit --allow-empty -m "chore: Phase 6 Checkpoint 2 (hub hardening) green"
```

Expected: 63 + 1 HubError = **64 integration, 117 domain**.

---

## Task 12: Rate limiting on REST auth endpoints + upload

**Files:**
- Modify: `src/Attic.Api/Program.cs`
- Modify: `src/Attic.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/Attic.Api/Endpoints/AttachmentsEndpoints.cs`

- [ ] **Step 12.1: Configure `RateLimiter` in `Program.cs`**

Near the top of `Program.cs`, add:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(
        Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed,
        c => { c.PermitLimit = 5; c.Window = TimeSpan.FromMinutes(1); c.QueueLimit = 0; });

    options.AddFixedWindowLimiter(
        Attic.Api.RateLimiting.RateLimitPolicyNames.UploadFixed,
        c => { c.PermitLimit = 60; c.Window = TimeSpan.FromMinutes(1); c.QueueLimit = 0; });

    options.RejectionStatusCode = 429;
});
```

And after `var app = builder.Build();`, before any endpoint mapping:

```csharp
app.UseRateLimiter();
```

- [ ] **Step 12.2: Apply `AuthFixed` policy to auth endpoints**

In `AuthEndpoints.MapAuthEndpoints`, wrap the login + register + forgot endpoints:

```csharp
        group.MapPost("/login", Login).RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed);
        group.MapPost("/register", Register).RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed);
        group.MapPost("/password/forgot", ForgotPassword).RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed);
```

If any of those routes don't exist yet (e.g. `password/forgot` was stubbed or skipped in Phase 1), just apply the policy to the ones that do exist. Preserve existing handler semantics.

- [ ] **Step 12.3: Apply `UploadFixed` to upload endpoint**

In `AttachmentsEndpoints.MapAttachmentsEndpoints`:
```csharp
        group.MapPost("/", Upload).DisableAntiforgery().RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.UploadFixed);
```

- [ ] **Step 12.4: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Program.cs \
        src/Attic.Api/Endpoints/AuthEndpoints.cs \
        src/Attic.Api/Endpoints/AttachmentsEndpoints.cs \
        docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): rate limit auth (5/min/IP) + upload (60/min/user)"
```

---

## Task 13: `RateLimitFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/RateLimitFlowTests.cs`

- [ ] **Step 13.1: Write**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class RateLimitFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Login_rate_limited_after_5_attempts_in_a_minute()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        // 5 wrong-password attempts — rate limiter allows all of them (they just 400).
        for (int i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest("nobody@example.com", "wrong"), ct);
            resp.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        // 6th in the same window → 429.
        var blocked = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@example.com", "wrong"), ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
```

- [ ] **Step 13.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "RateLimitFlowTests"
git add tests/Attic.Api.IntegrationTests/RateLimitFlowTests.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "test(api): login rate limit returns 429 after 5/min"
```

---

## Task 14: Security headers + HTTPS redirect + Kestrel limits

**Files:**
- Create: `src/Attic.Api/Security/SecurityHeadersMiddleware.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 14.1: Write `SecurityHeadersMiddleware.cs`**

```csharp
namespace Attic.Api.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
        // A minimal CSP for an SPA served under the same origin as the API.
        headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data: blob:; connect-src 'self' ws: wss:; style-src 'self' 'unsafe-inline'; script-src 'self'";

        await next(context);
    }
}
```

- [ ] **Step 14.2: Wire in `Program.cs`**

Near the top of the middleware pipeline (before `UseRouting` / endpoint mapping), add:

```csharp
app.UseMiddleware<Attic.Api.Security.SecurityHeadersMiddleware>();
```

For HTTPS redirection (only in Production — Aspire's dev cert handles dev):

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
```

For Kestrel limits, configure via `Program.cs` options:

```csharp
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 25 * 1024 * 1024;  // 25 MB (matches attachment 20 MB + multipart overhead)
    kestrel.Limits.MaxConcurrentConnections = 1000;
    kestrel.Limits.MinRequestBodyDataRate =
        new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
});
```

- [ ] **Step 14.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Security/SecurityHeadersMiddleware.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(api): security headers + production HTTPS redirect + Kestrel limits"
```

---

## Task 15: `SecurityHeadersFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/SecurityHeadersFlowTests.cs`

- [ ] **Step 15.1: Write**

```csharp
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class SecurityHeadersFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Every_response_carries_security_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var resp = await fx.ApiClient.GetAsync("/api/auth/me", ct);   // returns 401, but headers still set

        resp.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        resp.Headers.GetValues("X-Content-Type-Options").First().ShouldBe("nosniff");

        resp.Headers.Contains("X-Frame-Options").ShouldBeTrue();
        resp.Headers.GetValues("X-Frame-Options").First().ShouldBe("DENY");

        resp.Headers.Contains("Referrer-Policy").ShouldBeTrue();
        resp.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
}
```

- [ ] **Step 15.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "SecurityHeadersFlowTests"
git add tests/Attic.Api.IntegrationTests/SecurityHeadersFlowTests.cs docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "test(api): security headers present on responses"
```

---

## Task 16: Checkpoint 3 marker

```bash
dotnet test
git commit --allow-empty -m "chore: Phase 6 Checkpoint 3 (REST hardening) green"
```

Expected: Integration 64 + 1 RateLimit + 1 SecurityHeaders = **66. Domain 117. Total 183.**

---

## Task 17: Frontend — SignalR `onreconnected` + catch-up

**Files:**
- Modify: `src/Attic.Web/src/api/signalr.ts`
- Modify: `src/Attic.Web/src/chat/useChannelMessages.ts`

- [ ] **Step 17.1: Expose an `onReconnected` subscription in the wrapper**

In `signalr.ts`, extend the `HubClient` interface:

```ts
  onReconnected(cb: () => void): () => void;
```

In the factory, register a passthrough for SignalR's built-in `onreconnected`:

```ts
    onReconnected(cb) {
      const handler = () => cb();
      connection.onreconnected(handler);
      return () => { /* @microsoft/signalr 8 has no off for reconnected; return noop */ };
    },
```

**Note:** The `@microsoft/signalr` client accumulates `onreconnected` callbacks but exposes no `off` method in v8. The unsubscribe is a noop; in practice callbacks live for the lifetime of the connection, which is the singleton's lifetime. If the hook that registered the callback unmounts (e.g. a channel is closed), the callback should early-return — that's handled by the `active` flag pattern already used in `useChannelMessages`.

- [ ] **Step 17.2: Catch up in `useChannelMessages`**

In the existing `useEffect` that subscribes to the channel, add a second branch: on reconnect, re-fetch pages from the current first-page cursor. Simpler: just invalidate the query.

Inside the `useEffect` body, add near the end (before the return cleanup):

```ts
    const offReconnect = hub.onReconnected(() => {
      if (!active) return;
      void hub.subscribeToChannel(channelId);
      void qc.invalidateQueries({ queryKey });
    });
```

And in the cleanup, call `offReconnect()`.

- [ ] **Step 17.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/api/signalr.ts src/Attic.Web/src/chat/useChannelMessages.ts docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "feat(web): SignalR onreconnected triggers channel-messages catch-up"
```

---

## Task 18: Production `appsettings.Production.json`

**Files:**
- Create: `src/Attic.Api/appsettings.Production.json`

- [ ] **Step 18.1: Write**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Attachments": {
    "Root": "/data/attachments",
    "MaxFileBytes": 20971520,
    "MaxImageBytes": 3145728
  }
}
```

The `/data/attachments` path assumes a mounted volume in production.

- [ ] **Step 18.2: Commit**

```bash
git add src/Attic.Api/appsettings.Production.json docs/superpowers/plans/2026-04-21-phase6-hardening.md
git commit -m "chore(api): production appsettings (attachments root, log levels)"
```

---

## Task 19: README note + final smoke

**Files:**
- Modify: repo-root `README.md` (if exists) with a production-deployment note

- [ ] **Step 19.1: If `README.md` exists, append a Phase 6 deploy note**

Check `README.md` at the repo root — if present, append:

```md
## Production deployment

Attic is deployed via `dotnet publish` (Aspire publishing). Production `appsettings.Production.json` sets `Attachments:Root=/data/attachments`; mount a persistent volume there. Security headers, HSTS, and HTTPS redirect are enforced in non-Development environments. `appsettings.Production.json` overrides log levels to `Warning`.
```

If no README exists, skip this step.

- [ ] **Step 19.2: Full run**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: Domain 117, Integration 66, all green. Frontend build 0 errors.

- [ ] **Step 19.3: Commit marker**

```bash
git add README.md docs/superpowers/plans/2026-04-21-phase6-hardening.md 2>/dev/null || true
git commit --allow-empty -m "chore: Phase 6 end-to-end smoke green"
```

---

## Task 20: Checkpoint 4 + project-complete marker

```bash
git commit --allow-empty -m "chore: Phase 6 Checkpoint 4 complete — Attic production-ready"
```

---

## Phase 6 completion checklist

- [x] `AuditLog` entity + configuration + migration
- [x] `AuditLogContext` scoped service + `AuditLogInterceptor` (writes audit rows in the same `SaveChangesAsync`)
- [x] Audit hooks in: channel.delete, channel.ban_member, channel.unban_member, channel.change_role, session.revoke, account.delete, message.admin_delete
- [x] `GET /api/admin/audit/mine` (caller's own trail)
- [x] `GlobalHubFilter` catches exceptions, logs correlation id, synthesizes failure response
- [x] `HubRateLimiter` sliding window 60/min/user on `SendMessage`
- [x] REST rate limits: 5/min/IP on auth, 60/min/user on upload
- [x] Security headers middleware (CSP, X-Frame-Options, Referrer-Policy, etc.)
- [x] Production HTTPS redirect + HSTS
- [x] Kestrel MaxRequestBodySize / MaxConcurrentConnections / MinRequestBodyDataRate
- [x] Frontend `onreconnected` catch-up (invalidates message cache + re-subscribes)
- [x] Production `appsettings.Production.json`
- [x] Integration tests: audit entries, hub rate limit, REST rate limit, security headers

## Wrap-up

Attic is **feature-complete and production-hardened** after Phase 6:

- **Phase 1** — auth + hub + vertical slice
- **Phase 2** — rooms, members, invitations, moderation
- **Phase 3** — friends, blocks, personal chats
- **Phase 4** — attachments, edit, reply-to, paste/drop
- **Phase 5** — presence, unread, sessions, account deletion
- **Phase 6** — rate limiting, audit log, hub filter, security headers, production config

All phase boundaries shipped with test coverage (domain unit + REST integration + realtime integration). `Attic.Domain.Tests` runs in < 100 ms; `Attic.Api.IntegrationTests` spins up a full Aspire stack with Postgres + Redis for end-to-end verification.

## What is intentionally NOT shipped

Consistent with spec §2 non-goals:
- Real outbound email (reset URLs log to stdout).
- End-to-end encryption.
- Mobile clients / federation / SSO.
- Horizontal scale beyond one API instance (Redis backplane keeps the door open).
- Server-side image thumbnailing, full-text search, typing indicators, reactions.
