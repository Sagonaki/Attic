# Attic Phase 2 — Rooms, Members, Invitations, Moderation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a full room-management slice: any authenticated user can create public or private channels, browse a public catalog, join / leave rooms, invite others to private rooms, manage members (promote, demote, ban, unban), and admins can delete any message in a room. All mutations fan out in realtime via SignalR. Phase 1's hardcoded lobby goes away.

**Architecture:** Extends the Phase 1 stack — new REST endpoints under `/api/channels/*` and `/api/invitations/*`, new hub methods for message deletion, and server-to-client events `ChannelMemberJoined|Left|RoleChanged`, `RemovedFromChannel`, `ChannelDeleted`, `InvitationReceived`, `MessageDeleted`. Authorization lives in `Attic.Domain.Services.AuthorizationRules` as pure, unit-tested functions; controllers and the hub call into them after loading the minimal rows. Frontend gains a sidebar with public/private/personal tabs, a create-room modal, a public catalog view, a room details panel with admin controls, and an invitations inbox.

**Tech Stack:** Same as Phase 1 — .NET 10, Aspire 13.2.2, EF Core 10.0.5 + Npgsql, SignalR + Redis backplane, TanStack Query v5 + React Router v6 + `@microsoft/signalr` v8 + Tailwind 4. FluentValidation for request shape. xUnit v3 for tests.

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — Phase 2 boundary in §13, authorization rules in §8.1, REST surface in §9.1, SignalR contract in §9.2, group topology in §9.3.

---

## Prerequisites — invariants from Phase 1

Do not re-introduce any of these fixed defects on Phase 2 code:

- **DbContext registration** uses `AddDbContext<AtticDbContext>` + `EnrichNpgsqlDbContext<AtticDbContext>()` (NOT the auto-wired `AddNpgsqlDbContext<T>`, which forces pooling that breaks `OnConfiguring`/DI-resolved interceptors). Interceptor is attached in the options callback.
- **Cookie / session crypto** is in `Attic.Domain.Services.SessionToken` (pure, unit-tested). `Attic.Api.Auth.SessionFactory` is a thin wrapper that composes `SessionToken.Generate` + `SessionToken.FormatCookie` with `IClock` and builds a `Session` entity. Do not duplicate hashing / parsing logic elsewhere.
- **Hub methods read user id via `Context.User`** (SignalR invocation scopes don't populate the scoped `CurrentUser`). Use the existing `private Guid? UserId => CurrentUser.ReadUserId(Context.User!);` pattern.
- **Cookie `Secure` flag** is derived from `HttpRequest.IsHttps` (`CreateSessionCookieOptions(HttpRequest, DateTimeOffset)`), not hardcoded. Do not regress.
- **Raw SQL in configurations** must use snake_case identifiers — `UseSnakeCaseNamingConvention()` does NOT rewrite strings passed to `HasCheckConstraint` / `HasFilter`. Use `kind`, `deleted_at`, `revoked_at`, `content`, `banned_at`, etc. unquoted.
- **`TimestampInterceptor`** respects `IsModified` — if the caller already set `UpdatedAt`, the interceptor leaves it alone. This lets `Message.Edit` and (new in Phase 2) `Channel.Rename` / `Channel.UpdateDescription` own their `UpdatedAt` semantics.
- **Entity `UpdatedAt` properties** are `{ get; private set; }`. Mutation is via domain methods only; EF Core writes through the backing field.
- **`SendMessageRequestValidator`** is wired into `ChatHub.SendMessage` via `IValidator<SendMessageRequest>` injection, with explicit error codes (`invalid_channel`, `invalid_client_message_id`, `empty_content`, `content_too_large`) mapped onto `SendMessageResponse.Error`. Reuse the same pattern for new hub methods that accept shaped requests.
- **Aspire** is 13.2.2, package name is `Aspire.Hosting.JavaScript` (not `NodeJs`), AppHost uses `AddViteApp("web", "../Attic.Web")` (not `AddNpmApp(..., "dev")`).

When in doubt about an Aspire API, consult `~/.claude/skills/aspire/SKILL.md` first — training data is stale for Aspire 13.x.

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-2` (branched from merged `main`, which includes Phase 1).
- `dotnet test` → 33 Domain unit tests + 6 Integration tests, all green.
- `cd src/Attic.Web && npm run lint` (tsc --noEmit) exits 0.
- Podman machine running; `DOCKER_HOST` points at the podman socket.
- Phase 1 hardcoded lobby `11111111-1111-1111-1111-000000000001` still exists in seed; Phase 2 removes it.

---

## File structure additions

Extends the tree from Phase 1. New files listed; modified existing files noted.

```
src/
├── Attic.Domain/
│   ├── Entities/
│   │   └── ChannelInvitation.cs                                    (new)
│   ├── Enums/
│   │   └── InvitationStatus.cs                                     (new)
│   └── Services/
│       ├── AuthorizationResult.cs                                  (modify: enum extended)
│       └── AuthorizationRules.cs                                   (modify: 7 new rule methods)
├── Attic.Infrastructure/
│   └── Persistence/
│       ├── AtticDbContext.cs                                       (modify: DbSet<ChannelInvitation>)
│       ├── Configurations/
│       │   └── ChannelInvitationConfiguration.cs                   (new)
│       ├── Migrations/
│       │   └── XXXXXXXXXXXXXX_AddChannelInvitations.cs             (generated)
│       └── Seed/
│           └── SeedData.cs                                         (modify: remove Phase-1 lobby)
├── Attic.Contracts/
│   ├── Channels/
│   │   ├── ChannelSummary.cs                                       (new)
│   │   ├── ChannelDetails.cs                                       (new)
│   │   ├── CreateChannelRequest.cs                                 (new)
│   │   ├── UpdateChannelRequest.cs                                 (new)
│   │   ├── ChannelMemberSummary.cs                                 (new)
│   │   ├── BannedMemberSummary.cs                                  (new)
│   │   ├── ChangeRoleRequest.cs                                    (new)
│   │   └── UserSummary.cs                                          (new)
│   └── Invitations/
│       ├── InvitationDto.cs                                        (new)
│       └── InviteToChannelRequest.cs                               (new)
├── Attic.Api/
│   ├── Endpoints/
│   │   ├── ChannelsEndpoints.cs                                    (new — replaces the Phase-1 lobby assumptions)
│   │   ├── ChannelMembersEndpoints.cs                              (new)
│   │   ├── InvitationsEndpoints.cs                                 (new)
│   │   └── MessagesEndpoints.cs                                    (modify: membership check on GET)
│   ├── Hubs/
│   │   ├── ChatHub.cs                                              (modify: DeleteMessage, tightened Subscribe, event helpers)
│   │   └── ChannelEventBroadcaster.cs                              (new — IHubContext helper used from endpoints)
│   ├── Validators/
│   │   ├── CreateChannelRequestValidator.cs                        (new)
│   │   ├── UpdateChannelRequestValidator.cs                        (new)
│   │   ├── ChangeRoleRequestValidator.cs                           (new)
│   │   └── InviteToChannelRequestValidator.cs                      (new)
│   └── Program.cs                                                  (modify: map new endpoint groups)
└── Attic.Web/
    └── src/
        ├── api/
        │   ├── channels.ts                                         (new)
        │   ├── members.ts                                          (new)
        │   ├── invitations.ts                                      (new)
        │   └── signalr.ts                                          (modify: handlers for new events)
        ├── chat/
        │   ├── Sidebar.tsx                                         (new)
        │   ├── CreateRoomModal.tsx                                 (new)
        │   ├── PublicCatalog.tsx                                   (new)
        │   ├── RoomDetails.tsx                                     (new)
        │   ├── InvitationsInbox.tsx                                (new)
        │   ├── ChatShell.tsx                                       (modify: sidebar + route-driven channel switching)
        │   ├── ChatWindow.tsx                                      (modify: channelId from route, admin delete menu)
        │   ├── useChannelList.ts                                   (new)
        │   ├── useChannelDetails.ts                                (new)
        │   ├── useChannelMembers.ts                                (new)
        │   ├── useInvitations.ts                                   (new)
        │   ├── usePublicCatalog.ts                                 (new)
        │   └── useDeleteMessage.ts                                 (new)
        ├── App.tsx                                                 (modify: route /chat/:channelId)
        └── types.ts                                                (modify: new DTO types)
tests/
├── Attic.Domain.Tests/
│   ├── AuthorizationRulesTests.cs                                  (modify: 7 new rule test classes/regions)
│   ├── ChannelInvitationTests.cs                                   (new)
│   └── ChannelBehaviorTests.cs                                     (new — Rename, UpdateDescription)
└── Attic.Api.IntegrationTests/
    ├── ChannelCrudFlowTests.cs                                     (new)
    ├── MembershipFlowTests.cs                                      (new)
    ├── InvitationFlowTests.cs                                      (new)
    ├── ModerationFlowTests.cs                                      (new — admin delete, ban, role change)
    ├── RealtimeEventsFlowTests.cs                                  (new — RemovedFromChannel, MessageDeleted, ChannelDeleted)
    ├── MessagingFlowTests.cs                                       (modify: create own channel, drop Phase-1 lobby)
    └── AuthFlowTests.cs                                            (unchanged)
```

Total: ~40 new files, ~10 modified files. Central package manifest unchanged — no new third-party dependencies.

---

## Task ordering rationale

Phase 2 is TDD'd bottom-up: domain rules first (unit-tested and fast), then infrastructure (DbContext + migration), then API endpoints (integration-tested against a real Aspire stack), then realtime events, then the frontend. Each numbered task below is a single commit; Phase-1 commit-message conventions carry over (`feat(domain)`, `feat(infra)`, `feat(api)`, `feat(web)`, `test(api)`, `chore:`, `fix:`, `docs:`).

Four natural checkpoints map onto the review cycle (spec-reviewer + code-quality-reviewer between each):

- **Checkpoint 1 — Domain + Infra (Tasks 1-15):** entity, enum, seven authorization rules with tests, entity configuration, migration, seed cleanup, Phase-1 hub-shortcut removal.
- **Checkpoint 2 — Contracts + REST (Tasks 16-32):** all DTOs, validators, REST endpoints, integration tests for each flow.
- **Checkpoint 3 — Hub realtime (Tasks 33-40):** hub `DeleteMessage`, `ChannelEventBroadcaster`, wire broadcasts into REST endpoints, realtime integration tests.
- **Checkpoint 4 — Frontend (Tasks 41-50):** typed API clients, sidebar, create-room modal, public catalog, room details, invitations inbox, route-driven channel switching, SignalR event handlers, end-to-end smoke.

---

## Task 1: Extend `AuthorizationFailureReason` with Phase 2 codes

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationResult.cs`

The Phase 1 enum declared future codes as YAGNI-flagged placeholders; Phase 2 needs most of them used, and adds a few more. Replace the whole enum.

- [x] **Step 1.1: Replace `AuthorizationResult.cs` enum contents**

```csharp
namespace Attic.Domain.Services;

public enum AuthorizationFailureReason
{
    None = 0,
    NotAMember,
    BannedFromChannel,
    NotFriends,
    BlockedByOrBlockingUser,
    NotAuthor,
    NotAdmin,
    NotOwner,
    OwnerCannotLeave,
    OwnerCannotBeTargeted,
    OwnerCannotBeDemoted,
    AlreadyMember,
    AlreadyInvited,
    InvitationNotFound,
    ChannelNotFound,
    ChannelDeleted,
    CannotInviteToPublic,
    CannotAutoJoinPrivate,
    DuplicateFriendRequest
}

public readonly record struct AuthorizationResult(bool Allowed, AuthorizationFailureReason Reason)
{
    public static AuthorizationResult Ok() => new(true, AuthorizationFailureReason.None);
    public static AuthorizationResult Deny(AuthorizationFailureReason reason) => new(false, reason);
}
```

- [x] **Step 1.2: Build + test**

```bash
dotnet build src/Attic.Domain
dotnet test tests/Attic.Domain.Tests
```

Expected: 0/0, 33 tests still green.

- [x] **Step 1.3: Commit**

```bash
git add src/Attic.Domain/Services/AuthorizationResult.cs
git commit -m "feat(domain): extend AuthorizationFailureReason with Phase 2 codes"
```

---

## Task 2: `InvitationStatus` enum

**Files:**
- Create: `src/Attic.Domain/Enums/InvitationStatus.cs`

- [x] **Step 2.1: Write `InvitationStatus.cs`**

```csharp
namespace Attic.Domain.Enums;

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3
}
```

- [x] **Step 2.2: Build + commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Enums/InvitationStatus.cs
git commit -m "feat(domain): add InvitationStatus enum"
```

---

## Task 3: `ChannelInvitation` entity with unit tests (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/ChannelInvitation.cs`
- Create: `tests/Attic.Domain.Tests/ChannelInvitationTests.cs`

- [x] **Step 3.1: Write the failing tests — `tests/Attic.Domain.Tests/ChannelInvitationTests.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelInvitationTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Issue_creates_pending_invitation()
    {
        var inv = ChannelInvitation.Issue(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            channelId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            inviterId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            inviteeId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            now: T0);

        inv.Id.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        inv.ChannelId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        inv.InviterId.ShouldBe(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        inv.InviteeId.ShouldBe(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        inv.Status.ShouldBe(InvitationStatus.Pending);
        inv.CreatedAt.ShouldBe(T0);
        inv.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_self_invitation()
    {
        var self = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var act = () => ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), inviterId: self, inviteeId: self, now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("inviteeId");
    }

    [Fact]
    public void Accept_marks_accepted_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Accepted);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Decline_marks_declined_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Decline(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Declined);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Cancel_marks_cancelled_and_stamps_decidedAt()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Cancel(T0.AddMinutes(5));
        inv.Status.ShouldBe(InvitationStatus.Cancelled);
        inv.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Accept_is_idempotent_on_already_accepted()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        var act = () => inv.Accept(T0.AddMinutes(10));
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void Decline_rejects_non_pending()
    {
        var inv = ChannelInvitation.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        inv.Accept(T0.AddMinutes(5));
        Should.Throw<InvalidOperationException>(() => inv.Decline(T0.AddMinutes(10)));
    }
}
```

- [x] **Step 3.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "ChannelInvitationTests"
```

Expected: compile failure — `ChannelInvitation` does not exist.

- [x] **Step 3.3: Implement `src/Attic.Domain/Entities/ChannelInvitation.cs`**

```csharp
using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class ChannelInvitation
{
    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid InviterId { get; private set; }
    public Guid InviteeId { get; private set; }
    public InvitationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private ChannelInvitation() { }

    public static ChannelInvitation Issue(Guid id, Guid channelId, Guid inviterId, Guid inviteeId, DateTimeOffset now)
    {
        if (inviterId == inviteeId)
            throw new ArgumentException("Cannot invite yourself.", nameof(inviteeId));

        return new ChannelInvitation
        {
            Id = id,
            ChannelId = channelId,
            InviterId = inviterId,
            InviteeId = inviteeId,
            Status = InvitationStatus.Pending,
            CreatedAt = now
        };
    }

    public void Accept(DateTimeOffset at) => Decide(InvitationStatus.Accepted, at);
    public void Decline(DateTimeOffset at) => Decide(InvitationStatus.Declined, at);
    public void Cancel(DateTimeOffset at) => Decide(InvitationStatus.Cancelled, at);

    private void Decide(InvitationStatus next, DateTimeOffset at)
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Invitation is already {Status}.");
        Status = next;
        DecidedAt = at;
    }
}
```

- [x] **Step 3.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "ChannelInvitationTests"
```

Expected: 7 passing.

- [x] **Step 3.5: Commit**

```bash
git add src/Attic.Domain/Entities/ChannelInvitation.cs tests/Attic.Domain.Tests/ChannelInvitationTests.cs
git commit -m "feat(domain): add ChannelInvitation entity with state-machine behavior"
```

---

## Task 4: `Channel` entity — Rename + UpdateDescription (TDD)

**Files:**
- Modify: `src/Attic.Domain/Entities/Channel.cs`
- Create: `tests/Attic.Domain.Tests/ChannelBehaviorTests.cs`

- [x] **Step 4.1: Write the failing tests — `tests/Attic.Domain.Tests/ChannelBehaviorTests.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class ChannelBehaviorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    private static Channel NewRoom(ChannelKind kind = ChannelKind.Public) =>
        Channel.CreateRoom(Guid.NewGuid(), kind, "orig", "orig desc", Guid.NewGuid(), T0);

    [Fact]
    public void Rename_trims_and_sets_new_name()
    {
        var c = NewRoom();
        c.Rename("  new-name  ", T0.AddMinutes(1));
        c.Name.ShouldBe("new-name");
        c.UpdatedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_blank(string bad)
    {
        var c = NewRoom();
        Should.Throw<ArgumentException>(() => c.Rename(bad, T0.AddMinutes(1))).ParamName.ShouldBe("name");
    }

    [Fact]
    public void Rename_rejects_on_personal_channel()
    {
        var personal = Channel.CreatePersonal(Guid.NewGuid(), T0);
        Should.Throw<InvalidOperationException>(() => personal.Rename("x", T0.AddMinutes(1)));
    }

    [Fact]
    public void UpdateDescription_trims_and_sets()
    {
        var c = NewRoom();
        c.UpdateDescription("  new desc  ", T0.AddMinutes(1));
        c.Description.ShouldBe("new desc");
        c.UpdatedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void UpdateDescription_accepts_null_to_clear()
    {
        var c = NewRoom();
        c.UpdateDescription(null, T0.AddMinutes(1));
        c.Description.ShouldBeNull();
    }
}
```

- [x] **Step 4.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "ChannelBehaviorTests"
```

Expected: compile failure (Rename, UpdateDescription don't exist).

- [x] **Step 4.3: Implement `Rename` and `UpdateDescription` on `Channel`**

Open `src/Attic.Domain/Entities/Channel.cs`. After the `SoftDelete` method (end of class), append:

```csharp
    public void Rename(string newName, DateTimeOffset at)
    {
        if (Kind == ChannelKind.Personal)
            throw new InvalidOperationException("Personal channels cannot be renamed.");
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name is required.", nameof(newName));
        Name = newName.Trim();
        UpdatedAt = at;
    }

    public void UpdateDescription(string? newDescription, DateTimeOffset at)
    {
        if (Kind == ChannelKind.Personal)
            throw new InvalidOperationException("Personal channels do not have a description.");
        Description = newDescription?.Trim();
        UpdatedAt = at;
    }
```

- [x] **Step 4.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: 33 prior + 7 invitation tests + 6 channel-behavior tests = 46 passing.

- [x] **Step 4.5: Commit**

```bash
git add src/Attic.Domain/Entities/Channel.cs tests/Attic.Domain.Tests/ChannelBehaviorTests.cs
git commit -m "feat(domain): add Channel.Rename and UpdateDescription with behavior tests"
```

---

## Task 5: Authorization — `CanJoinChannel` + `CanLeaveChannel` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [x] **Step 5.1: Append failing tests to `AuthorizationRulesTests.cs`**

After the existing `AuthorizationRulesTests` class contents, add new test regions:

```csharp
    private static readonly DateTimeOffset T0_J = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void CanJoinChannel_allows_public_room_when_not_a_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, existingMember: null);
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanJoinChannel_denies_already_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var member = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, member);
        result.Reason.ShouldBe(AuthorizationFailureReason.AlreadyMember);
    }

    [Fact]
    public void CanJoinChannel_denies_banned_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        var member = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        member.Ban(Guid.NewGuid(), "spam", T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, member);
        result.Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanJoinChannel_denies_private_room_without_invitation()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        var result = AuthorizationRules.CanJoinChannel(channel, existingMember: null);
        result.Reason.ShouldBe(AuthorizationFailureReason.CannotAutoJoinPrivate);
    }

    [Fact]
    public void CanLeaveChannel_allows_non_owner_member()
    {
        var channelId = Guid.NewGuid();
        var member = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanLeaveChannel(member).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanLeaveChannel_denies_owner()
    {
        var channelId = Guid.NewGuid();
        var owner = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanLeaveChannel(owner).Reason.ShouldBe(AuthorizationFailureReason.OwnerCannotLeave);
    }

    [Fact]
    public void CanLeaveChannel_denies_nonmember()
    {
        AuthorizationRules.CanLeaveChannel(null).Reason.ShouldBe(AuthorizationFailureReason.NotAMember);
    }
```

Also add the `using` directives at the top of `AuthorizationRulesTests.cs` if not already present: `using Attic.Domain.Entities;`, `using Attic.Domain.Enums;`.

- [x] **Step 5.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanJoinChannel|CanLeaveChannel"
```

Expected: compile failure.

- [x] **Step 5.3: Append methods to `src/Attic.Domain/Services/AuthorizationRules.cs`**

After `CanPostInChannel`, add:

```csharp
    public static AuthorizationResult CanJoinChannel(Channel channel, ChannelMember? existingMember)
    {
        if (channel.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.ChannelDeleted);
        if (existingMember is not null)
        {
            if (existingMember.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
            return AuthorizationResult.Deny(AuthorizationFailureReason.AlreadyMember);
        }
        if (channel.Kind == Enums.ChannelKind.Private)
            return AuthorizationResult.Deny(AuthorizationFailureReason.CannotAutoJoinPrivate);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanLeaveChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.Role == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotLeave);
        return AuthorizationResult.Ok();
    }
```

Add `using Attic.Domain.Enums;` at the top if not present.

- [x] **Step 5.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: all prior tests + 7 new = green.

- [x] **Step 5.5: Commit**

```bash
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanJoinChannel and CanLeaveChannel rules"
```

---

## Task 6: Authorization — `CanManageChannel` + `CanDeleteChannel` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [x] **Step 6.1: Append tests**

```csharp
    [Fact]
    public void CanManageChannel_allows_admin_and_owner()
    {
        var admin = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanManageChannel(admin).Allowed.ShouldBeTrue();
        var owner = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanManageChannel(owner).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanManageChannel_denies_plain_member()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanManageChannel(member).Reason.ShouldBe(AuthorizationFailureReason.NotAdmin);
    }

    [Fact]
    public void CanManageChannel_denies_banned_admin()
    {
        var admin = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Admin, T0_J);
        admin.Ban(Guid.NewGuid(), "spam", T0_J);
        AuthorizationRules.CanManageChannel(admin).Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanDeleteChannel_allows_owner()
    {
        var channelId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var channel = Channel.CreateRoom(channelId, ChannelKind.Public, "room", null, ownerId, T0_J);
        AuthorizationRules.CanDeleteChannel(channel, actorUserId: ownerId).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanDeleteChannel_denies_non_owner()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "room", null, Guid.NewGuid(), T0_J);
        AuthorizationRules.CanDeleteChannel(channel, actorUserId: Guid.NewGuid()).Reason
            .ShouldBe(AuthorizationFailureReason.NotOwner);
    }
```

- [x] **Step 6.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanManageChannel|CanDeleteChannel"
```

Expected: compile failure.

- [x] **Step 6.3: Append methods to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanManageChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
        if (member.Role == Enums.ChannelRole.Member) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAdmin);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanDeleteChannel(Channel channel, Guid actorUserId)
    {
        if (channel.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.ChannelDeleted);
        if (channel.OwnerId != actorUserId) return AuthorizationResult.Deny(AuthorizationFailureReason.NotOwner);
        return AuthorizationResult.Ok();
    }
```

- [x] **Step 6.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

- [x] **Step 6.5: Commit**

```bash
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanManageChannel and CanDeleteChannel rules"
```

---

## Task 7: Authorization — `CanBanFromChannel` + `CanUnbanFromChannel` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [ ] **Step 7.1: Append tests**

```csharp
    [Fact]
    public void CanBanFromChannel_allows_admin_banning_member()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanBanFromChannel_allows_owner_banning_admin()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanBanFromChannel_denies_banning_owner()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var owner = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, owner).Reason
            .ShouldBe(AuthorizationFailureReason.OwnerCannotBeTargeted);
    }

    [Fact]
    public void CanBanFromChannel_denies_non_admin_actor()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanBanFromChannel(actor, target).Reason.ShouldBe(AuthorizationFailureReason.NotAdmin);
    }

    [Fact]
    public void CanUnbanFromChannel_allows_admin()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanUnbanFromChannel(actor).Allowed.ShouldBeTrue();
    }
```

- [ ] **Step 7.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanBanFromChannel|CanUnbanFromChannel"
```

- [ ] **Step 7.3: Append methods to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanBanFromChannel(ChannelMember? actor, ChannelMember? target)
    {
        var manage = CanManageChannel(actor);
        if (!manage.Allowed) return manage;
        if (target is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (target.Role == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeTargeted);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanUnbanFromChannel(ChannelMember? actor) => CanManageChannel(actor);
```

- [ ] **Step 7.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanBanFromChannel and CanUnbanFromChannel rules"
```

---

## Task 8: Authorization — `CanChangeRole` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [ ] **Step 8.1: Append tests**

```csharp
    [Fact]
    public void CanChangeRole_admin_can_promote_member_to_admin()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanChangeRole(actor, target, ChannelRole.Admin).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanChangeRole_admin_can_demote_other_admin_to_member()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanChangeRole(actor, target, ChannelRole.Member).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanChangeRole_cannot_demote_owner()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        AuthorizationRules.CanChangeRole(actor, target, ChannelRole.Member).Reason
            .ShouldBe(AuthorizationFailureReason.OwnerCannotBeDemoted);
    }

    [Fact]
    public void CanChangeRole_cannot_promote_to_owner()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Owner, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanChangeRole(actor, target, ChannelRole.Owner).Reason
            .ShouldBe(AuthorizationFailureReason.OwnerCannotBeTargeted);
    }

    [Fact]
    public void CanChangeRole_denies_non_admin_actor()
    {
        var channelId = Guid.NewGuid();
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var target = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanChangeRole(actor, target, ChannelRole.Admin).Reason
            .ShouldBe(AuthorizationFailureReason.NotAdmin);
    }
```

- [ ] **Step 8.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanChangeRole"
```

- [ ] **Step 8.3: Append method to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanChangeRole(ChannelMember? actor, ChannelMember? target, Enums.ChannelRole newRole)
    {
        var manage = CanManageChannel(actor);
        if (!manage.Allowed) return manage;
        if (target is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (target.Role == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeDemoted);
        if (newRole == Enums.ChannelRole.Owner) return AuthorizationResult.Deny(AuthorizationFailureReason.OwnerCannotBeTargeted);
        return AuthorizationResult.Ok();
    }
```

- [ ] **Step 8.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanChangeRole rule"
```

---

## Task 9: Authorization — `CanDeleteMessage` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [ ] **Step 9.1: Append tests**

```csharp
    [Fact]
    public void CanDeleteMessage_author_can_delete_own()
    {
        var authorId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var message = Message.Post(channelId, authorId, "hi", null, T0_J);
        AuthorizationRules.CanDeleteMessage(message, actorUserId: authorId, actorMembership: null, channelKind: ChannelKind.Public)
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanDeleteMessage_admin_of_room_can_delete_any()
    {
        var channelId = Guid.NewGuid();
        var message = Message.Post(channelId, Guid.NewGuid(), "hi", null, T0_J);
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanDeleteMessage(message, actor.UserId, actor, ChannelKind.Public)
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanDeleteMessage_personal_channel_has_no_admins()
    {
        var channelId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var message = Message.Post(channelId, authorId, "hi", null, T0_J);
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Admin, T0_J);
        AuthorizationRules.CanDeleteMessage(message, actor.UserId, actor, ChannelKind.Personal).Reason
            .ShouldBe(AuthorizationFailureReason.NotAuthor);
    }

    [Fact]
    public void CanDeleteMessage_non_author_non_admin_denied()
    {
        var channelId = Guid.NewGuid();
        var message = Message.Post(channelId, Guid.NewGuid(), "hi", null, T0_J);
        var actor = ChannelMember.Join(channelId, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanDeleteMessage(message, actor.UserId, actor, ChannelKind.Public).Reason
            .ShouldBe(AuthorizationFailureReason.NotAuthor);
    }
```

- [ ] **Step 9.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanDeleteMessage"
```

- [ ] **Step 9.3: Append method to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanDeleteMessage(
        Message message,
        Guid actorUserId,
        ChannelMember? actorMembership,
        Enums.ChannelKind channelKind)
    {
        if (message.SenderId == actorUserId) return AuthorizationResult.Ok();
        // Room admins (not personal) can delete any message.
        if (channelKind != Enums.ChannelKind.Personal && actorMembership is not null
            && actorMembership.BannedAt is null && actorMembership.Role != Enums.ChannelRole.Member)
        {
            return AuthorizationResult.Ok();
        }
        return AuthorizationResult.Deny(AuthorizationFailureReason.NotAuthor);
    }
```

- [ ] **Step 9.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanDeleteMessage rule"
```

---

## Task 10: Authorization — `CanInviteToChannel` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [ ] **Step 10.1: Append tests**

```csharp
    [Fact]
    public void CanInviteToChannel_member_of_private_can_invite_non_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        var inviter = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanInviteToChannel(channel, inviter, inviteeExistingMembership: null, hasPendingInvitation: false)
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanInviteToChannel_denies_inviting_to_public_room()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Public, "lobby", null, Guid.NewGuid(), T0_J);
        var inviter = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanInviteToChannel(channel, inviter, null, false).Reason
            .ShouldBe(AuthorizationFailureReason.CannotInviteToPublic);
    }

    [Fact]
    public void CanInviteToChannel_denies_non_member_inviter()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        AuthorizationRules.CanInviteToChannel(channel, inviter: null, null, false).Reason
            .ShouldBe(AuthorizationFailureReason.NotAMember);
    }

    [Fact]
    public void CanInviteToChannel_denies_invitee_already_member()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        var inviter = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        var existing = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanInviteToChannel(channel, inviter, existing, false).Reason
            .ShouldBe(AuthorizationFailureReason.AlreadyMember);
    }

    [Fact]
    public void CanInviteToChannel_denies_invitee_with_pending_invitation()
    {
        var channel = Channel.CreateRoom(Guid.NewGuid(), ChannelKind.Private, "vip", null, Guid.NewGuid(), T0_J);
        var inviter = ChannelMember.Join(channel.Id, Guid.NewGuid(), ChannelRole.Member, T0_J);
        AuthorizationRules.CanInviteToChannel(channel, inviter, null, hasPendingInvitation: true).Reason
            .ShouldBe(AuthorizationFailureReason.AlreadyInvited);
    }
```

- [ ] **Step 10.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanInviteToChannel"
```

- [ ] **Step 10.3: Append method to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanInviteToChannel(
        Channel channel,
        ChannelMember? inviter,
        ChannelMember? inviteeExistingMembership,
        bool hasPendingInvitation)
    {
        if (channel.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.ChannelDeleted);
        if (channel.Kind != Enums.ChannelKind.Private) return AuthorizationResult.Deny(AuthorizationFailureReason.CannotInviteToPublic);
        if (inviter is null || inviter.BannedAt is not null)
            return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (inviteeExistingMembership is not null && inviteeExistingMembership.BannedAt is null)
            return AuthorizationResult.Deny(AuthorizationFailureReason.AlreadyMember);
        if (hasPendingInvitation) return AuthorizationResult.Deny(AuthorizationFailureReason.AlreadyInvited);
        return AuthorizationResult.Ok();
    }
```

- [ ] **Step 10.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add CanInviteToChannel rule"
```

---

## Task 11: `ChannelInvitationConfiguration` — EF Core mapping

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/ChannelInvitationConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [ ] **Step 11.1: Write `ChannelInvitationConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelInvitationConfiguration : IEntityTypeConfiguration<ChannelInvitation>
{
    public void Configure(EntityTypeBuilder<ChannelInvitation> b)
    {
        b.ToTable("channel_invitations");
        b.HasKey(i => i.Id);
        b.Property(i => i.Status).HasConversion<int>().IsRequired();

        // Incoming-invitations list for a user.
        b.HasIndex(i => new { i.InviteeId, i.Status })
            .HasDatabaseName("ix_channel_invitations_invitee_status");

        // Unique-pending-per-(channel,invitee) — snake_case raw SQL because UseSnakeCaseNamingConvention
        // does NOT rewrite strings passed to HasFilter.
        b.HasIndex(i => new { i.ChannelId, i.InviteeId })
            .IsUnique()
            .HasDatabaseName("ux_channel_invitations_channel_invitee_pending")
            .HasFilter($"status = {(int)InvitationStatus.Pending}");
    }
}
```

- [ ] **Step 11.2: Add `DbSet<ChannelInvitation>` to `AtticDbContext`**

Open `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`. Insert after `DbSet<Message>`:

```csharp
    public DbSet<ChannelInvitation> ChannelInvitations => Set<ChannelInvitation>();
```

- [ ] **Step 11.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/ChannelInvitationConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs
git commit -m "feat(infra): add ChannelInvitation EF Core configuration"
```

---

## Task 12: EF Core migration — `AddChannelInvitations`

**Files:**
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/*_AddChannelInvitations.cs`
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/AtticDbContextModelSnapshot.cs` (auto-updated)

- [ ] **Step 12.1: Generate the migration**

```bash
dotnet tool run dotnet-ef migrations add AddChannelInvitations \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 12.2: Generate the idempotent SQL script and sanity-check**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase2-invitations.sql
grep -i -A1 "channel_invitations\|status =" /tmp/phase2-invitations.sql | head -25
```

Expected excerpts:
- `CREATE TABLE channel_invitations` with columns `id`, `channel_id`, `inviter_id`, `invitee_id`, `status`, `created_at`, `decided_at`, `updated_at`, all snake_case.
- `CREATE INDEX ix_channel_invitations_invitee_status ON channel_invitations (invitee_id, status);`
- `CREATE UNIQUE INDEX ux_channel_invitations_channel_invitee_pending ON channel_invitations (channel_id, invitee_id) WHERE status = 0;`

If the filter clause reads `WHERE "Status" = 0` or quotes any column, STOP — that's the Phase-2-facing recurrence of the Checkpoint-2 snake_case-raw-SQL bug. Fix in the configuration (see Task 11) and regenerate.

- [ ] **Step 12.3: Build + commit**

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Migrations
git commit -m "feat(infra): migration AddChannelInvitations"
```

---

## Task 13: Remove the Phase-1 seeded lobby

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Seed/SeedData.cs`

Phase 2 introduces real CRUD, so the hardcoded lobby with `OwnerId = Guid.Empty` is no longer needed. Tests that posted to it will be updated in Checkpoint 2 to create their own channel.

- [ ] **Step 13.1: Replace `SeedData.cs` contents**

```csharp
namespace Attic.Infrastructure.Persistence.Seed;

/// <summary>
/// No-op in Phase 2. Users create their own channels via the REST API.
/// Kept as a static extension point for future phases (system accounts, default rooms, etc.).
/// </summary>
public static class SeedData
{
    public static Task EnsureSeededAsync(AtticDbContext db, CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 13.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Seed/SeedData.cs
git commit -m "chore(infra): drop Phase-1 seeded lobby (Phase 2 replaces with real CRUD)"
```

---

## Task 14: `ChatHub.SendMessage` — remove Phase-1 auto-join shortcut

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`
- Modify: `tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs`

Phase 1's hub auto-created a `ChannelMember` when a sender posted to a channel they weren't in. Phase 2 replaces that with proper `/api/channels/{id}/join`; posting without membership must now return `{ok:false, error:"NotAMember"}`.

- [ ] **Step 14.1: Replace the auto-join branch in `ChatHub.SendMessage`**

Open `src/Attic.Api/Hubs/ChatHub.cs`. Find the Phase-1 fallback block starting with `// Phase 1 fallback: the seeded lobby has no members yet` and replace the block with:

```csharp
        var member = await db.ChannelMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == request.ChannelId && m.UserId == userId.Value);

        var auth = AuthorizationRules.CanPostInChannel(member);
        if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());
```

(Removes the `if (member is null) { auto-join }` block — membership must already exist.)

- [ ] **Step 14.2: Update `MessagingFlowTests.cs` to create a channel first**

The two existing tests hit the seeded lobby. Replace the hardcoded `LobbyId` with a freshly-created channel per test.

Open `tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs`. Replace the private `LobbyId` field and both test bodies so each test:

1. Registers a user (already done).
2. Calls `POST /api/channels` with a unique name to create a fresh public room owned by that user.
3. Uses the returned channel id wherever the old tests used `LobbyId`.

Full file contents:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class MessagingFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Send_message_over_hub_persists_and_is_readable_over_REST()
    {
        var ct = TestContext.Current.CancellationToken;

        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"m-{Guid.NewGuid():N}@example.com";
        var username = $"m{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var createResponse = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"room-{Guid.NewGuid():N}"[..20], "Test room", "public"), ct);
        createResponse.EnsureSuccessStatusCode();
        var channel = (await createResponse.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();

        await connection.StartAsync(ct);

        var received = new TaskCompletionSource<MessageDto>();
        connection.On<MessageDto>("MessageCreated", dto => received.TrySetResult(dto));

        var sub = await connection.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        sub.GetProperty("ok").GetBoolean().ShouldBeTrue();

        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hello world", null), ct);

        response.Ok.ShouldBeTrue();
        response.ServerId.ShouldNotBeNull();

        var echo = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        echo.Content.ShouldBe("hello world");
        echo.SenderUsername.ShouldBe(username);

        var get = await client.GetAsync($"/api/channels/{channel.Id:D}/messages?limit=10", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await get.Content.ReadFromJsonAsync<PagedResult<MessageDto>>(ct);
        page.ShouldNotBeNull();
        page!.Items.ShouldContain(m => m.Content == "hello world");
    }

    [Fact]
    public async Task Send_message_over_3KB_returns_ok_false_with_content_too_large()
    {
        var ct = TestContext.Current.CancellationToken;

        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"big-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"big{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var createResponse = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"big-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        createResponse.EnsureSuccessStatusCode();
        var channel = (await createResponse.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();

        await connection.StartAsync(ct);
        await connection.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var huge = new string('x', 3200);
        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), huge, null), ct);

        response.Ok.ShouldBeFalse();
        response.Error.ShouldBe("content_too_large");
    }
}
```

Note: this test file references `Attic.Contracts.Channels.CreateChannelRequest` and `ChannelDetails` which do not yet exist — they're added in Task 16. The test project will fail to compile until Task 16 lands, and this Task 14's verification step below accepts that (we only rebuild the API, not the tests). See Task 17 for the green-test checkpoint.

- [ ] **Step 14.3: Build the API-only subgraph**

```bash
dotnet build src/Attic.Api
```

Expected: 0/0. Integration tests will fail to compile until Task 16 adds the DTOs.

- [ ] **Step 14.4: Commit**

```bash
git add src/Attic.Api/Hubs/ChatHub.cs tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs
git commit -m "refactor(api): drop Phase-1 auto-join shortcut from ChatHub.SendMessage"
```

---

## Task 15: `MessagesEndpoints` — tighten to require membership

**Files:**
- Modify: `src/Attic.Api/Endpoints/MessagesEndpoints.cs`

Phase 1 allowed any authenticated user to read messages from any channel. Phase 2 restricts reads to non-banned members (including admins/owners). Read access remains broader than write for admins viewing banned users (via `.IgnoreQueryFilters`) — Phase 2 does NOT need that; catalog scan is distinct and goes through `/api/channels/public`.

- [ ] **Step 15.1: Replace `MessagesEndpoints.GetBeforeCursor`**

Open `src/Attic.Api/Endpoints/MessagesEndpoints.cs`. Replace the handler body with:

```csharp
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
```

- [ ] **Step 15.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/MessagesEndpoints.cs
git commit -m "fix(api): require channel membership to read message history"
```

---

## Task 16: Contracts — Phase 2 DTOs

**Files:**
- Create: `src/Attic.Contracts/Channels/UserSummary.cs`
- Create: `src/Attic.Contracts/Channels/ChannelSummary.cs`
- Create: `src/Attic.Contracts/Channels/ChannelDetails.cs`
- Create: `src/Attic.Contracts/Channels/CreateChannelRequest.cs`
- Create: `src/Attic.Contracts/Channels/UpdateChannelRequest.cs`
- Create: `src/Attic.Contracts/Channels/ChannelMemberSummary.cs`
- Create: `src/Attic.Contracts/Channels/BannedMemberSummary.cs`
- Create: `src/Attic.Contracts/Channels/ChangeRoleRequest.cs`
- Create: `src/Attic.Contracts/Invitations/InvitationDto.cs`
- Create: `src/Attic.Contracts/Invitations/InviteToChannelRequest.cs`

All are `sealed record` types under their respective subfolders.

- [ ] **Step 16.1: Write `Channels/UserSummary.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record UserSummary(Guid Id, string Username);
```

- [ ] **Step 16.2: Write `Channels/ChannelSummary.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record ChannelSummary(
    Guid Id,
    string Kind,      // "public" | "private" | "personal"
    string? Name,
    string? Description,
    Guid? OwnerId,
    int MemberCount,
    int UnreadCount);
```

- [ ] **Step 16.3: Write `Channels/ChannelDetails.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record ChannelDetails(
    Guid Id,
    string Kind,
    string? Name,
    string? Description,
    Guid? OwnerId,
    DateTimeOffset CreatedAt,
    int MemberCount);
```

- [ ] **Step 16.4: Write `Channels/CreateChannelRequest.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record CreateChannelRequest(string Name, string? Description, string Kind);
```

- [ ] **Step 16.5: Write `Channels/UpdateChannelRequest.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record UpdateChannelRequest(string? Name, string? Description);
```

- [ ] **Step 16.6: Write `Channels/ChannelMemberSummary.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record ChannelMemberSummary(
    Guid UserId,
    string Username,
    string Role,           // "owner" | "admin" | "member"
    DateTimeOffset JoinedAt);
```

- [ ] **Step 16.7: Write `Channels/BannedMemberSummary.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record BannedMemberSummary(
    Guid UserId,
    string Username,
    Guid BannedById,
    string? BannedByUsername,
    DateTimeOffset BannedAt,
    string? Reason);
```

- [ ] **Step 16.8: Write `Channels/ChangeRoleRequest.cs`**

```csharp
namespace Attic.Contracts.Channels;

public sealed record ChangeRoleRequest(string Role);   // "admin" | "member"
```

- [ ] **Step 16.9: Write `Invitations/InvitationDto.cs`**

```csharp
namespace Attic.Contracts.Invitations;

public sealed record InvitationDto(
    Guid Id,
    Guid ChannelId,
    string ChannelName,
    Guid InviterId,
    string InviterUsername,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
```

- [ ] **Step 16.10: Write `Invitations/InviteToChannelRequest.cs`**

```csharp
namespace Attic.Contracts.Invitations;

public sealed record InviteToChannelRequest(string Username);
```

- [ ] **Step 16.11: Build + commit**

```bash
dotnet build src/Attic.Contracts
git add src/Attic.Contracts
git commit -m "feat(contracts): add Phase 2 DTOs for channels and invitations"
```

---

## Task 17: Validators for Phase 2 requests

**Files:**
- Create: `src/Attic.Api/Validators/CreateChannelRequestValidator.cs`
- Create: `src/Attic.Api/Validators/UpdateChannelRequestValidator.cs`
- Create: `src/Attic.Api/Validators/ChangeRoleRequestValidator.cs`
- Create: `src/Attic.Api/Validators/InviteToChannelRequestValidator.cs`

Rules (from spec §4.1 and §8.4):

- Channel name: 3-120 chars, no leading/trailing whitespace (trim handled by domain). Use regex `^[A-Za-z0-9_\- ]{3,120}$` (letters, digits, underscore, hyphen, space).
- Description: up to 1024 chars, optional.
- Kind (`CreateChannelRequest`): `public` or `private`. Personal channels are NOT created via this endpoint (Phase 3).
- Role (`ChangeRoleRequest`): `admin` or `member`. `owner` is forbidden (enforced domain-side too).
- Username (`InviteToChannelRequest`): non-empty, matches user username regex.

- [ ] **Step 17.1: Write `CreateChannelRequestValidator.cs`**

```csharp
using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().Matches("^[A-Za-z0-9_\\- ]{3,120}$").WithErrorCode("invalid_name");
        RuleFor(r => r.Description).MaximumLength(1024).WithErrorCode("description_too_long");
        RuleFor(r => r.Kind)
            .Must(k => k == "public" || k == "private")
            .WithErrorCode("invalid_kind");
    }
}
```

- [ ] **Step 17.2: Write `UpdateChannelRequestValidator.cs`**

```csharp
using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class UpdateChannelRequestValidator : AbstractValidator<UpdateChannelRequest>
{
    public UpdateChannelRequestValidator()
    {
        RuleFor(r => r.Name)
            .Matches("^[A-Za-z0-9_\\- ]{3,120}$").When(r => r.Name is not null)
            .WithErrorCode("invalid_name");
        RuleFor(r => r.Description).MaximumLength(1024).WithErrorCode("description_too_long");
    }
}
```

- [ ] **Step 17.3: Write `ChangeRoleRequestValidator.cs`**

```csharp
using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(r => r.Role)
            .Must(r => r == "admin" || r == "member")
            .WithErrorCode("invalid_role");
    }
}
```

- [ ] **Step 17.4: Write `InviteToChannelRequestValidator.cs`**

```csharp
using Attic.Contracts.Invitations;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class InviteToChannelRequestValidator : AbstractValidator<InviteToChannelRequest>
{
    public InviteToChannelRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
    }
}
```

- [ ] **Step 17.5: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Validators
git commit -m "feat(api): add FluentValidation validators for Phase 2 requests"
```

---

## Task 18: `POST /api/channels` — create channel endpoint + integration test

**Files:**
- Create: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Create: `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`

This task scaffolds the `ChannelsEndpoints` static class and maps the first route (`POST /api/channels`). Subsequent tasks append sibling routes to the same group.

- [ ] **Step 18.1: Write `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`**

```csharp
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
```

- [ ] **Step 18.2: Map the group in `Program.cs`**

Open `src/Attic.Api/Program.cs`. After `app.MapMessagesEndpoints();` add:

```csharp
app.MapChannelsEndpoints();
```

- [ ] **Step 18.3: Write integration test — `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class ChannelCrudFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Create_channel_returns_details_and_makes_creator_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"room-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "test", "public"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        details.ShouldNotBeNull();
        details!.Name.ShouldBe(name);
        details.Kind.ShouldBe("public");
        details.MemberCount.ShouldBe(1);
        details.OwnerId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_channel_rejects_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"dup-{Guid.NewGuid():N}"[..20];

        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct)).EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_channel_rejects_invalid_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var response = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest("ok-name", null, "personal"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await response.Content.ReadFromJsonAsync<ApiError>(ct);
        err.ShouldNotBeNull();
        err!.Code.ShouldBe("invalid_kind");
    }

    internal static async Task<(HttpClient Client, string Username)> RegisterFresh(CancellationToken ct)
    {
        // Build a dedicated HttpClient whose CookieContainer isn't shared with the fixture's client.
        // Pulled via a per-test helper so all Phase 2 flow tests can reuse it.
        throw new NotImplementedException("Fill in via shared helper in step 18.4.");
    }
}
```

- [ ] **Step 18.4: Move `RegisterFresh` helper into a shared static class**

Create `tests/Attic.Api.IntegrationTests/TestHelpers.cs`:

```csharp
using System.Net.Http.Json;
using Attic.Contracts.Auth;

namespace Attic.Api.IntegrationTests;

internal static class TestHelpers
{
    public static async Task<(HttpClient Client, string Username, string Email)> RegisterFresh(AppHostFixture fx, CancellationToken ct)
    {
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"u-{Guid.NewGuid():N}@example.com";
        var username = $"u{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        return (client, username, email);
    }
}
```

Update `ChannelCrudFlowTests.RegisterFresh` to delegate: replace the inner-class method body with a thin forward.

```csharp
    internal static async Task<(HttpClient Client, string Username)> RegisterFresh(CancellationToken ct)
    {
        var (client, username, _) = await TestHelpers.RegisterFresh(fx: null!, ct);
        return (client, username);
    }
```

But note `RegisterFresh` needs access to the fixture's `BaseAddress`. The shared helper takes `AppHostFixture` — change `ChannelCrudFlowTests.RegisterFresh` to a non-static method that captures `fx`:

```csharp
    internal async Task<(HttpClient Client, string Username)> RegisterFresh(CancellationToken ct)
    {
        var (client, username, _) = await TestHelpers.RegisterFresh(fx, ct);
        return (client, username);
    }
```

And update the call sites (`await RegisterFresh(ct);`) accordingly. They're already instance calls on the test class, so this works.

- [ ] **Step 18.5: Run integration tests**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "ChannelCrudFlowTests|MessagingFlowTests|AuthFlowTests"
```

Expected: all green (3 new + 2 messaging + 4 auth = 9). The MessagingFlowTests from Task 14 also start passing now that `CreateChannelRequest`/`ChannelDetails` exist and the endpoint is mapped.

- [ ] **Step 18.6: Commit**

```bash
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs \
        tests/Attic.Api.IntegrationTests/TestHelpers.cs
git commit -m "feat(api): POST /api/channels creates a room and makes the caller its owner"
```

---

## Task 19: `GET /api/channels/public?search=&cursor=` — public catalog

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`

Keyset-paginated catalog of public channels, filtered by name prefix. Uses the filtered unique index `ux_channels_name_not_personal` on `(name)` where `kind <> 2 AND deleted_at IS NULL` — the `INCLUDE (description, kind)` clause means the catalog query returns from the index without heap access.

- [ ] **Step 19.1: Append `GetPublicCatalog` to `ChannelsEndpoints.cs`**

In `MapChannelsEndpoints`, before `return routes;`, add:

```csharp
        group.MapGet("/public", GetPublicCatalog).AllowAnonymous();
```

Append the handler method inside the class (after `CreateChannel`):

```csharp
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

        // Keyset pagination on `Id` (Guid v7 — sortable). Decode cursor as base64url Guid bytes.
        if (!string.IsNullOrEmpty(cursor) && Guid.TryParse(cursor, out var cursorId))
        {
            query = query.Where(c => c.Id.CompareTo(cursorId) > 0);
        }

        var rows = await query
            .OrderBy(c => c.Id)
            .Take(size)
            .Select(c => new { c.Id, Name = c.Name!, c.Description })
            .ToListAsync(ct);

        // Second query to count members per channel (index ix_channel_members_user + composite PK covers this).
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
```

- [ ] **Step 19.2: Append integration tests to `ChannelCrudFlowTests.cs`**

Inside the class, add:

```csharp
    [Fact]
    public async Task Public_catalog_lists_created_public_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"cat-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "catalog-test", "public"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/channels/public?search={name[..8]}&limit=50", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.Items.ShouldContain(i => i.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task Public_catalog_excludes_private_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var privateName = $"priv-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(privateName, null, "private"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/channels/public?search={privateName[..8]}", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.Items.ShouldBeEmpty();
    }
```

Add `using System.Text.Json;` at the top if missing.

- [ ] **Step 19.3: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "ChannelCrudFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs
git commit -m "feat(api): GET /api/channels/public paginated catalog with prefix search"
```

---

## Task 20: `GET /api/channels/mine` + `GET /api/channels/{id}`

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`

- [ ] **Step 20.1: Append routes in `MapChannelsEndpoints`**

```csharp
        group.MapGet("/mine", GetMyChannels);
        group.MapGet("/{id:guid}", GetChannelDetails);
```

- [ ] **Step 20.2: Append handlers to `ChannelsEndpoints.cs`**

```csharp
    private static async Task<IResult> GetMyChannels(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var userId = currentUser.UserIdOrThrow;
        var rows = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Channels.AsNoTracking(),
                  m => m.ChannelId,
                  c => c.Id,
                  (m, c) => new { m.Role, c.Id, c.Kind, c.Name, c.Description, c.OwnerId })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.ChannelMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ChannelId))
            .GroupBy(m => m.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.ChannelId, c => c.Count);

        var items = rows.Select(r => new ChannelSummary(
            r.Id,
            r.Kind.ToString().ToLowerInvariant(),
            r.Name,
            r.Description,
            r.OwnerId,
            countMap.TryGetValue(r.Id, out var n) ? n : 0,
            UnreadCount: 0  // Phase 5 wires real unread counts.
        )).ToList();

        return Results.Ok(items);
    }

    private static async Task<IResult> GetChannelDetails(
        Guid id,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var userId = currentUser.UserIdOrThrow;

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return Results.NotFound();

        var isMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == id && m.UserId == userId, ct);

        // Private channels: only current members may view details.
        // Public channels: any authenticated user may view details.
        if (channel.Kind == ChannelKind.Private && !isMember) return Results.Forbid();

        var memberCount = await db.ChannelMembers.AsNoTracking().CountAsync(m => m.ChannelId == id, ct);

        return Results.Ok(new ChannelDetails(
            channel.Id,
            channel.Kind.ToString().ToLowerInvariant(),
            channel.Name,
            channel.Description,
            channel.OwnerId,
            channel.CreatedAt,
            memberCount));
    }
```

- [ ] **Step 20.3: Append tests**

```csharp
    [Fact]
    public async Task GET_channels_mine_returns_created_channels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"mine-{Guid.NewGuid():N}"[..20];
        (await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct)).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/channels/mine", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.ShouldContain(c => c.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task GET_channels_id_returns_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);

        var name = $"det-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "detail", "public"), ct);
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var detail = await client.GetAsync($"/api/channels/{channel.Id:D}", ct);
        detail.EnsureSuccessStatusCode();
        var body = await detail.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        body.ShouldNotBeNull();
        body!.Name.ShouldBe(name);
        body.Description.ShouldBe("detail");
    }

    [Fact]
    public async Task GET_channels_id_private_forbidden_to_non_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);

        var name = $"pd-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "private"), ct);
        create.EnsureSuccessStatusCode();
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var response = await outsider.GetAsync($"/api/channels/{channel.Id:D}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 20.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ChannelCrudFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs
git commit -m "feat(api): GET /api/channels/mine and /api/channels/{id}"
```

---

## Task 21: `PATCH /api/channels/{id}` — rename / update description (owner-only)

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`

- [ ] **Step 21.1: Append route**

```csharp
        group.MapPatch("/{id:guid}", UpdateChannel);
```

- [ ] **Step 21.2: Append handler**

```csharp
    private static async Task<IResult> UpdateChannel(
        Guid id,
        [FromBody] UpdateChannelRequest req,
        IValidator<UpdateChannelRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var channel = await db.Channels.AsTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return Results.NotFound();

        var auth = Attic.Domain.Services.AuthorizationRules.CanDeleteChannel(channel, currentUser.UserIdOrThrow);
        // Reuse CanDeleteChannel because update is also owner-only.
        if (!auth.Allowed) return Results.Forbid();

        if (req.Name is not null)
        {
            var trimmed = req.Name.Trim();
            var nameTaken = await db.Channels
                .AnyAsync(c => c.Name == trimmed && c.Id != id && c.Kind != ChannelKind.Personal, ct);
            if (nameTaken) return Results.Conflict(new ApiError("name_taken", "Channel name is already taken."));
            channel.Rename(trimmed, clock.UtcNow);
        }
        if (req.Description is not null)
        {
            channel.UpdateDescription(req.Description, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct);

        var memberCount = await db.ChannelMembers.AsNoTracking().CountAsync(m => m.ChannelId == id, ct);
        return Results.Ok(new ChannelDetails(
            channel.Id,
            channel.Kind.ToString().ToLowerInvariant(),
            channel.Name,
            channel.Description,
            channel.OwnerId,
            channel.CreatedAt,
            memberCount));
    }
```

- [ ] **Step 21.3: Append tests**

```csharp
    [Fact]
    public async Task PATCH_channel_renames_and_updates_description()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"pre-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, "old", "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var newName = $"post-{Guid.NewGuid():N}"[..20];
        var patch = await client.PatchAsJsonAsync($"/api/channels/{channel.Id:D}",
            new UpdateChannelRequest(newName, "new"), ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<ChannelDetails>(ct);
        updated.ShouldNotBeNull();
        updated!.Name.ShouldBe(newName);
        updated.Description.ShouldBe("new");
    }

    [Fact]
    public async Task PATCH_channel_forbidden_to_non_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);
        var name = $"fb-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var patch = await outsider.PatchAsJsonAsync($"/api/channels/{channel.Id:D}",
            new UpdateChannelRequest("new-name", null), ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 21.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ChannelCrudFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs
git commit -m "feat(api): PATCH /api/channels/{id} for name/description updates (owner only)"
```

---

## Task 22: `DELETE /api/channels/{id}` — owner soft-deletes channel

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs`

- [ ] **Step 22.1: Append route**

```csharp
        group.MapDelete("/{id:guid}", DeleteChannel);
```

- [ ] **Step 22.2: Append handler**

```csharp
    private static async Task<IResult> DeleteChannel(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var channel = await db.Channels.AsTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return Results.NotFound();

        var auth = Attic.Domain.Services.AuthorizationRules.CanDeleteChannel(channel, currentUser.UserIdOrThrow);
        if (!auth.Allowed) return Results.Forbid();

        channel.SoftDelete(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 22.3: Append tests**

```csharp
    [Fact]
    public async Task DELETE_channel_soft_deletes_and_hides_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterFresh(ct);
        var name = $"del-{Guid.NewGuid():N}"[..20];
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var del = await client.DeleteAsync($"/api/channels/{channel.Id:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var get = await client.GetAsync($"/api/channels/{channel.Id:D}", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_channel_forbidden_to_non_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await RegisterFresh(ct);
        var name = $"delFb-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _) = await RegisterFresh(ct);
        var del = await outsider.DeleteAsync($"/api/channels/{channel.Id:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 22.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ChannelCrudFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/ChannelCrudFlowTests.cs
git commit -m "feat(api): DELETE /api/channels/{id} soft-deletes room (owner only)"
```

---

## Task 23: `POST /api/channels/{id}/join` — public join

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Create: `tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs`

- [ ] **Step 23.1: Append route**

```csharp
        group.MapPost("/{id:guid}/join", JoinChannel);
```

- [ ] **Step 23.2: Append handler**

```csharp
    private static async Task<IResult> JoinChannel(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return Results.NotFound();

        var userId = currentUser.UserIdOrThrow;
        var existing = await db.ChannelMembers.IgnoreQueryFilters().AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == id && m.UserId == userId, ct);

        var auth = Attic.Domain.Services.AuthorizationRules.CanJoinChannel(channel, existing);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot join channel."));

        // `existing` is null (CanJoinChannel returned Ok only when existing is null + public + not deleted).
        var member = ChannelMember.Join(id, userId, ChannelRole.Member, clock.UtcNow);
        db.ChannelMembers.Add(member);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 23.3: Write `MembershipFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class MembershipFlowTests(AppHostFixture fx)
{
    private async Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => await TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Join_public_channel_succeeds_and_shows_up_in_mine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var name = $"pub-{Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest(name, null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        var join = await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", content: null, ct);
        join.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await joiner.GetAsync("/api/channels/mine", ct);
        mine.EnsureSuccessStatusCode();
        var body = await mine.Content.ReadFromJsonAsync<List<JsonElement>>(ct);
        body.ShouldNotBeNull();
        body!.ShouldContain(c => c.GetProperty("id").GetGuid() == channel.Id);
    }

    [Fact]
    public async Task Join_private_channel_rejected_without_invitation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"pr-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (outsider, _, _) = await Register(ct);
        var join = await outsider.PostAsync($"/api/channels/{channel.Id:D}/join", content: null, ct);
        join.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await join.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("CannotAutoJoinPrivate");
    }
}
```

- [ ] **Step 23.4: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "MembershipFlowTests|ChannelCrudFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs
git commit -m "feat(api): POST /api/channels/{id}/join (public rooms)"
```

---

## Task 24: `POST /api/channels/{id}/leave` — leave channel

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs`

- [ ] **Step 24.1: Append route**

```csharp
        group.MapPost("/{id:guid}/leave", LeaveChannel);
```

- [ ] **Step 24.2: Append handler**

```csharp
    private static async Task<IResult> LeaveChannel(
        Guid id,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var userId = currentUser.UserIdOrThrow;
        var member = await db.ChannelMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == id && m.UserId == userId, ct);

        var auth = Attic.Domain.Services.AuthorizationRules.CanLeaveChannel(member);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot leave channel."));

        db.ChannelMembers.Remove(member!);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 24.3: Append tests**

```csharp
    [Fact]
    public async Task Leave_channel_removes_membership()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"lv-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var leave = await joiner.PostAsync($"/api/channels/{channel.Id:D}/leave", null, ct);
        leave.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await joiner.GetAsync("/api/channels/mine", ct);
        var body = await mine.Content.ReadFromJsonAsync<List<JsonElement>>(ct);
        body!.ShouldNotContain(c => c.GetProperty("id").GetGuid() == channel.Id);
    }

    [Fact]
    public async Task Owner_cannot_leave_their_own_room()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"own-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var leave = await owner.PostAsync($"/api/channels/{channel.Id:D}/leave", null, ct);
        leave.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await leave.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("OwnerCannotLeave");
    }
```

- [ ] **Step 24.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "MembershipFlowTests"
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs
git commit -m "feat(api): POST /api/channels/{id}/leave"
```

---

## Task 25: `ChannelMembersEndpoints` — list members

**Files:**
- Create: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Modify: `tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs`

This task scaffolds the members endpoint group and adds `GET /api/channels/{id}/members`. Subsequent tasks 26-28 append sibling routes.

- [ ] **Step 25.1: Write `ChannelMembersEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class ChannelMembersEndpoints
{
    public static IEndpointRouteBuilder MapChannelMembersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/channels/{channelId:guid}").RequireAuthorization();

        group.MapGet("/members", ListMembers);

        return routes;
    }

    private static async Task<IResult> ListMembers(
        Guid channelId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null) return Results.NotFound();

        var userId = currentUser.UserIdOrThrow;
        var callerIsMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        // Private rooms: only members see the list. Public rooms: any authenticated user.
        if (channel.Kind == ChannelKind.Private && !callerIsMember) return Results.Forbid();

        var rows = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.UserId, u => u.Id,
                  (m, u) => new ChannelMemberSummary(
                      u.Id, u.Username, m.Role.ToString().ToLowerInvariant(), m.JoinedAt))
            .OrderByDescending(m => m.Role)   // owner / admin / member ordering
            .ThenBy(m => m.Username)
            .ToListAsync(ct);

        return Results.Ok(rows);
    }
}
```

- [ ] **Step 25.2: Map the group in `Program.cs`**

Add after `app.MapChannelsEndpoints();`:

```csharp
app.MapChannelMembersEndpoints();
```

- [ ] **Step 25.3: Append tests to `MembershipFlowTests.cs`**

```csharp
    [Fact]
    public async Task GET_members_lists_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, ownerUsername, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"lm-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, joinerUsername, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var members = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        members.EnsureSuccessStatusCode();
        var body = await members.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        body.ShouldNotBeNull();
        body!.ShouldContain(m => m.Username == ownerUsername && m.Role == "owner");
        body.ShouldContain(m => m.Username == joinerUsername && m.Role == "member");
    }
```

- [ ] **Step 25.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "MembershipFlowTests"
git add src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/MembershipFlowTests.cs
git commit -m "feat(api): GET /api/channels/{id}/members"
```

---

## Task 26: `DELETE /api/channels/{id}/members/{userId}` — admin removes member (counts as ban)

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs`
- Create: `tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs`

Per spec §2.4.8, removing a member from a room by an admin is recorded as a ban — the member's `ChannelMember` row stays but gets `BannedAt` set, so they cannot rejoin without unban.

- [ ] **Step 26.1: Append route**

```csharp
        group.MapDelete("/members/{userId:guid}", BanMember);
```

- [ ] **Step 26.2: Append handler**

```csharp
    private static async Task<IResult> BanMember(
        Guid channelId,
        Guid userId,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var target = await db.ChannelMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        var auth = AuthorizationRules.CanBanFromChannel(actor, target);
        if (!auth.Allowed) return Results.Forbid();

        target!.Ban(actorId, reason: null, at: clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 26.3: Write `ModerationFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class ModerationFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Admin_bans_member_and_member_cannot_post_or_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ban-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        var members = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        // Joiner hasn't joined yet — register above only registers the user.
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        // Owner bans joiner.
        var joinerMembers = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var memberList = await joinerMembers.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerUserId = memberList!.First(m => m.Role == "member").UserId;

        var ban = await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerUserId:D}", ct);
        ban.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Banned user can no longer rejoin.
        var rejoin = await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct);
        rejoin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_admin_cannot_ban()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"na-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (m1, _, _) = await Register(ct);
        (await m1.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var (m2, _, _) = await Register(ct);
        (await m2.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var anyMember = members!.First(m => m.Role == "member");

        var ban = await m1.DeleteAsync($"/api/channels/{channel.Id:D}/members/{anyMember.UserId:D}", ct);
        ban.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

Close the class with `}` — actual file-end.

- [ ] **Step 26.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ModerationFlowTests"
git add src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs
git commit -m "feat(api): DELETE /api/channels/{id}/members/{userId} (admin ban)"
```

---

## Task 27: `POST /api/channels/{id}/members/{userId}/role` — admin promote / demote

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs`

- [ ] **Step 27.1: Append route**

```csharp
        group.MapPost("/members/{userId:guid}/role", ChangeMemberRole);
```

- [ ] **Step 27.2: Append handler**

```csharp
    private static async Task<IResult> ChangeMemberRole(
        Guid channelId,
        Guid userId,
        [FromBody] ChangeRoleRequest req,
        IValidator<ChangeRoleRequest> validator,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var newRole = req.Role == "admin" ? ChannelRole.Admin : ChannelRole.Member;

        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var target = await db.ChannelMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        var auth = AuthorizationRules.CanChangeRole(actor, target, newRole);
        if (!auth.Allowed) return Results.Forbid();

        target!.ChangeRole(newRole);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 27.3: Append test**

```csharp
    [Fact]
    public async Task Owner_can_promote_member_to_admin_and_demote_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rl-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, _, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Role == "member").UserId;

        var promote = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/members/{joinerId:D}/role",
            new ChangeRoleRequest("admin"), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterPromote = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var afterPromoteBody = await afterPromote.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        afterPromoteBody!.First(m => m.UserId == joinerId).Role.ShouldBe("admin");

        var demote = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/members/{joinerId:D}/role",
            new ChangeRoleRequest("member"), ct);
        demote.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
```

- [ ] **Step 27.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ModerationFlowTests"
git add src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs
git commit -m "feat(api): POST /api/channels/{id}/members/{userId}/role"
```

---

## Task 28: `GET /api/channels/{id}/bans` + `DELETE /api/channels/{id}/bans/{userId}`

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs`
- Modify: `tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs`

- [ ] **Step 28.1: Append routes**

```csharp
        group.MapGet("/bans", ListBans);
        group.MapDelete("/bans/{userId:guid}", UnbanMember);
```

- [ ] **Step 28.2: Append handlers**

```csharp
    private static async Task<IResult> ListBans(
        Guid channelId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var auth = AuthorizationRules.CanManageChannel(actor);
        if (!auth.Allowed) return Results.Forbid();

        // Use IgnoreQueryFilters to see rows with BannedAt set.
        var rows = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.BannedAt != null)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.UserId, u => u.Id,
                  (m, u) => new { m.UserId, UserUsername = u.Username, m.BannedById, m.BannedAt, m.BanReason })
            .ToListAsync(ct);

        // Resolve BannedByUsername in a second query (small cardinality).
        var bannerIds = rows.Where(r => r.BannedById.HasValue).Select(r => r.BannedById!.Value).Distinct().ToList();
        var banners = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => bannerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var summary = rows.Select(r => new BannedMemberSummary(
            r.UserId,
            r.UserUsername,
            r.BannedById ?? Guid.Empty,
            r.BannedById.HasValue && banners.TryGetValue(r.BannedById.Value, out var bn) ? bn : null,
            r.BannedAt ?? default,
            r.BanReason)).ToList();

        return Results.Ok(summary);
    }

    private static async Task<IResult> UnbanMember(
        Guid channelId,
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var actorId = currentUser.UserIdOrThrow;
        var actor = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == actorId, ct);
        var auth = AuthorizationRules.CanUnbanFromChannel(actor);
        if (!auth.Allowed) return Results.Forbid();

        // Load target with filters off (banned rows are hidden by global filter).
        var target = await db.ChannelMembers.IgnoreQueryFilters().AsTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);
        if (target is null || target.BannedAt is null) return Results.NotFound();

        // Per spec §2.4.8: unban removes the BannedAt flag; row persists, role reverts to Member.
        target.Unban();
        // Unban keeps the row — now representing an active member. Tests Task 28.3 verify this.
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
```

- [ ] **Step 28.3: Append tests**

```csharp
    [Fact]
    public async Task Bans_list_shows_banned_users_and_unban_allows_rejoin()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ub-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, joinerName, _) = await Register(ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Username == joinerName).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        var bans = await owner.GetAsync($"/api/channels/{channel.Id:D}/bans", ct);
        bans.EnsureSuccessStatusCode();
        var bansBody = await bans.Content.ReadFromJsonAsync<List<BannedMemberSummary>>(ct);
        bansBody.ShouldNotBeNull();
        bansBody!.ShouldContain(b => b.UserId == joinerId);

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/bans/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        // After unban, user should be re-listed as member (row was preserved and flipped back to Member role).
        var afterList = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var afterBody = await afterList.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        afterBody!.ShouldContain(m => m.UserId == joinerId && m.Role == "member");
    }
```

- [ ] **Step 28.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "ModerationFlowTests"
git add src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs tests/Attic.Api.IntegrationTests/ModerationFlowTests.cs
git commit -m "feat(api): GET /api/channels/{id}/bans + DELETE .../bans/{userId}"
```

---

## Task 29: `InvitationsEndpoints` — create + list + decide

**Files:**
- Create: `src/Attic.Api/Endpoints/InvitationsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Create: `tests/Attic.Api.IntegrationTests/InvitationFlowTests.cs`

Four routes in one task because each one is small and the tests cross-cut. `POST /api/channels/{channelId}/invitations` hangs off the channels group; the rest live under `/api/invitations`.

- [ ] **Step 29.1: Write `InvitationsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Invitations;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class InvitationsEndpoints
{
    public static IEndpointRouteBuilder MapInvitationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var channels = routes.MapGroup("/api/channels/{channelId:guid}").RequireAuthorization();
        channels.MapPost("/invitations", Issue);

        var invites = routes.MapGroup("/api/invitations").RequireAuthorization();
        invites.MapGet("/", ListMine);
        invites.MapPost("/{id:guid}/accept", Accept);
        invites.MapPost("/{id:guid}/decline", Decline);

        return routes;
    }

    private static async Task<IResult> Issue(
        Guid channelId,
        [FromBody] InviteToChannelRequest req,
        IValidator<InviteToChannelRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null) return Results.NotFound();

        var inviterId = currentUser.UserIdOrThrow;
        var inviter = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == inviterId, ct);

        var invitee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (invitee is null) return Results.NotFound();
        if (invitee.Id == inviterId) return Results.BadRequest(new ApiError("self_invite", "Cannot invite yourself."));

        var inviteeMembership = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == invitee.Id, ct);

        var hasPending = await db.ChannelInvitations.AsNoTracking()
            .AnyAsync(i => i.ChannelId == channelId && i.InviteeId == invitee.Id && i.Status == InvitationStatus.Pending, ct);

        var auth = AuthorizationRules.CanInviteToChannel(channel, inviter, inviteeMembership, hasPending);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot invite."));

        var inv = ChannelInvitation.Issue(Guid.NewGuid(), channelId, inviterId, invitee.Id, clock.UtcNow);
        db.ChannelInvitations.Add(inv);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new InvitationDto(
            inv.Id, channelId, channel.Name ?? "", inviterId,
            Username(await db.Users.AsNoTracking().FirstAsync(u => u.Id == inviterId, ct)),
            inv.Status.ToString().ToLowerInvariant(), inv.CreatedAt, inv.DecidedAt));
    }

    private static string Username(User u) => u.Username;

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var rows = await db.ChannelInvitations.AsNoTracking()
            .Where(i => i.InviteeId == userId && i.Status == InvitationStatus.Pending)
            .Join(db.Channels.AsNoTracking(), i => i.ChannelId, c => c.Id, (i, c) => new { Inv = i, Channel = c })
            .Join(db.Users.IgnoreQueryFilters(), x => x.Inv.InviterId, u => u.Id,
                  (x, u) => new InvitationDto(
                      x.Inv.Id, x.Channel.Id, x.Channel.Name ?? "",
                      x.Inv.InviterId, u.Username,
                      x.Inv.Status.ToString().ToLowerInvariant(),
                      x.Inv.CreatedAt, x.Inv.DecidedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Accept(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var inv = await db.ChannelInvitations.AsTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.InviteeId == userId, ct);
        if (inv is null) return Results.NotFound();

        if (inv.Status != InvitationStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Invitation is no longer pending."));

        // Accept + create membership in the same transaction.
        inv.Accept(clock.UtcNow);
        var existing = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.ChannelId == inv.ChannelId && m.UserId == userId, ct);
        if (!existing)
        {
            db.ChannelMembers.Add(ChannelMember.Join(inv.ChannelId, userId, ChannelRole.Member, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> Decline(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var inv = await db.ChannelInvitations.AsTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.InviteeId == userId, ct);
        if (inv is null) return Results.NotFound();

        if (inv.Status != InvitationStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Invitation is no longer pending."));

        inv.Decline(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
```

- [ ] **Step 29.2: Map in `Program.cs`**

```csharp
app.MapInvitationsEndpoints();
```

- [ ] **Step 29.3: Write `InvitationFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Invitations;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class InvitationFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Invite_accept_adds_user_to_private_room()
    {
        var ct = TestContext.Current.CancellationToken;

        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"vip-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (invitee, inviteeUsername, _) = await Register(ct);

        var invite = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        invite.EnsureSuccessStatusCode();

        var inbox = await invitee.GetAsync("/api/invitations", ct);
        inbox.EnsureSuccessStatusCode();
        var pending = (await inbox.Content.ReadFromJsonAsync<List<InvitationDto>>(ct))!;
        pending.ShouldNotBeEmpty();
        var inv = pending.First(p => p.ChannelId == channel.Id);

        var accept = await invitee.PostAsync($"/api/invitations/{inv.Id:D}/accept", null, ct);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Invitee should now be a member.
        var mine = await invitee.GetAsync("/api/channels/mine", ct);
        var body = await mine.Content.ReadFromJsonAsync<List<ChannelSummary>>(ct);
        body!.ShouldContain(c => c.Id == channel.Id);
    }

    [Fact]
    public async Task Decline_invitation_does_not_add_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"dec-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (invitee, inviteeUsername, _) = await Register(ct);
        (await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct)).EnsureSuccessStatusCode();

        var inbox = await invitee.GetAsync("/api/invitations", ct);
        var pending = (await inbox.Content.ReadFromJsonAsync<List<InvitationDto>>(ct))!;
        var inv = pending.First();

        var decline = await invitee.PostAsync($"/api/invitations/{inv.Id:D}/decline", null, ct);
        decline.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mine = await invitee.GetAsync("/api/channels/mine", ct);
        var body = await mine.Content.ReadFromJsonAsync<List<ChannelSummary>>(ct);
        body!.ShouldNotContain(c => c.Id == channel.Id);
    }

    [Fact]
    public async Task Cannot_invite_to_public_room()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"pub-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (_, inviteeUsername, _) = await Register(ct);
        var invite = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        invite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await invite.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("CannotInviteToPublic");
    }

    [Fact]
    public async Task Duplicate_pending_invitation_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"dup-{Guid.NewGuid():N}"[..20], null, "private"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (_, inviteeUsername, _) = await Register(ct);

        (await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct)).EnsureSuccessStatusCode();

        var second = await owner.PostAsJsonAsync(
            $"/api/channels/{channel.Id:D}/invitations",
            new InviteToChannelRequest(inviteeUsername), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = await second.Content.ReadFromJsonAsync<ApiError>(ct);
        err!.Code.ShouldBe("AlreadyInvited");
    }
}
```

- [ ] **Step 29.4: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "InvitationFlowTests"
git add src/Attic.Api/Endpoints/InvitationsEndpoints.cs src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/InvitationFlowTests.cs
git commit -m "feat(api): invitations endpoints (create, list, accept, decline)"
```

---

## Task 30: Full Checkpoint 2 test sweep

**Files:** none (verification only)

- [ ] **Step 30.1: Run the full suite**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: Domain tests ~48 (33 Phase 1 + 7 invitation + 5 channel-behavior + ~15 new authorization facts), Integration tests ~22 (6 Phase 1 + 3 create + 2 public catalog + 3 mine/detail + 2 patch + 2 delete + 2 join/private-forbidden + 2 leave + 1 list-members + 2 ban + 1 role + 1 unban + 4 invitations). All green.

Actual counts will vary slightly depending on how the plan's tests land; the important thing is zero failures.

- [ ] **Step 30.2: Commit the Checkpoint 2 marker**

```bash
git commit --allow-empty -m "chore: Phase 2 Checkpoint 2 (REST + integration tests) green"
```

---

## Task 31: `ChannelEventBroadcaster` — IHubContext helper

**Files:**
- Create: `src/Attic.Api/Hubs/ChannelEventBroadcaster.cs`
- Modify: `src/Attic.Api/Program.cs`

REST endpoints need to emit SignalR events after committing mutations (e.g. `ChannelMemberJoined` after `POST /join`). Hub can't be injected directly into endpoints; use `IHubContext<ChatHub>` wrapped in a small named service.

- [ ] **Step 31.1: Write `src/Attic.Api/Hubs/ChannelEventBroadcaster.cs`**

```csharp
using Attic.Contracts.Channels;
using Attic.Contracts.Invitations;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class ChannelEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task ChannelMemberJoined(Guid channelId, ChannelMemberSummary summary) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberJoined", channelId, summary);

    public Task ChannelMemberLeft(Guid channelId, Guid userId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberLeft", channelId, userId);

    public Task ChannelMemberRoleChanged(Guid channelId, Guid userId, string role) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelMemberRoleChanged", channelId, userId, role);

    public Task ChannelDeleted(Guid channelId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("ChannelDeleted", channelId);

    public Task RemovedFromChannel(Guid userId, Guid channelId, string reason) =>
        hub.Clients.Group(GroupNames.User(userId)).SendAsync("RemovedFromChannel", channelId, reason);

    public Task InvitationReceived(Guid userId, InvitationDto invitation) =>
        hub.Clients.Group(GroupNames.User(userId)).SendAsync("InvitationReceived", invitation);

    public Task MessageDeleted(Guid channelId, long messageId) =>
        hub.Clients.Group(GroupNames.Channel(channelId)).SendAsync("MessageDeleted", channelId, messageId);
}
```

- [ ] **Step 31.2: Register in `Program.cs`**

Add after `builder.Services.AddSignalR(...)` but before `var app = builder.Build();`:

```csharp
builder.Services.AddScoped<Attic.Api.Hubs.ChannelEventBroadcaster>();
```

- [ ] **Step 31.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChannelEventBroadcaster.cs src/Attic.Api/Program.cs
git commit -m "feat(api): add ChannelEventBroadcaster IHubContext helper"
```

---

## Task 32: Wire broadcasts into channel CRUD and membership endpoints

**Files:**
- Modify: `src/Attic.Api/Endpoints/ChannelsEndpoints.cs`
- Modify: `src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs`
- Modify: `src/Attic.Api/Endpoints/InvitationsEndpoints.cs`

- [ ] **Step 32.1: Inject `ChannelEventBroadcaster` and emit on each mutation**

Edit each endpoint group. Handler injections get an extra parameter `ChannelEventBroadcaster events`, and each handler publishes the matching event after `SaveChangesAsync`:

**`DeleteChannel`** (ChannelsEndpoints): after `channel.SoftDelete(...); await db.SaveChangesAsync(...)`, call:

```csharp
        await events.ChannelDeleted(channel.Id);
```

**`JoinChannel`** (ChannelsEndpoints): after `db.ChannelMembers.Add(member); await db.SaveChangesAsync(...)`, resolve username and broadcast:

```csharp
        var username = (await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct)).Username;
        await events.ChannelMemberJoined(id, new ChannelMemberSummary(
            userId, username, "member", member.JoinedAt));
```

**`LeaveChannel`** (ChannelsEndpoints): after `db.ChannelMembers.Remove(member!); await db.SaveChangesAsync(...)`, call:

```csharp
        await events.ChannelMemberLeft(id, userId);
```

**`BanMember`** (ChannelMembersEndpoints): after `target!.Ban(...); await db.SaveChangesAsync(...)`:

```csharp
        await events.ChannelMemberLeft(channelId, userId);
        await events.RemovedFromChannel(userId, channelId, "banned");
```

**`ChangeMemberRole`** (ChannelMembersEndpoints): after `target!.ChangeRole(newRole); await db.SaveChangesAsync(...)`:

```csharp
        await events.ChannelMemberRoleChanged(channelId, userId, newRole.ToString().ToLowerInvariant());
```

**`UnbanMember`** (ChannelMembersEndpoints): after `target.Unban(); await db.SaveChangesAsync(...)`, emit both a `ChannelMemberJoined` (since the user is now a member again) and resolve the username:

```csharp
        var uname = (await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == userId, ct)).Username;
        await events.ChannelMemberJoined(channelId, new ChannelMemberSummary(
            userId, uname, "member", target.JoinedAt));
```

**`Issue`** (InvitationsEndpoints): after `await db.SaveChangesAsync(ct);` before the return:

```csharp
        var dto = new InvitationDto(
            inv.Id, channelId, channel.Name ?? "", inviterId,
            Username(await db.Users.AsNoTracking().FirstAsync(u => u.Id == inviterId, ct)),
            inv.Status.ToString().ToLowerInvariant(), inv.CreatedAt, inv.DecidedAt);

        await events.InvitationReceived(invitee.Id, dto);

        return Results.Ok(dto);
```

(Replace the existing return with this block — it avoids the double-query pattern.)

**`Accept`** (InvitationsEndpoints): after `await db.SaveChangesAsync(ct);` emit `ChannelMemberJoined` for the accepting user:

```csharp
        var username = (await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct)).Username;
        await events.ChannelMemberJoined(inv.ChannelId, new ChannelMemberSummary(
            userId, username, "member", clock.UtcNow));
```

- [ ] **Step 32.2: Add the broadcaster parameter to each handler signature**

Every modified handler now takes `ChannelEventBroadcaster events` as a parameter. FluentValidation-style DI auto-injection resolves it. Example for `DeleteChannel`:

```csharp
    private static async Task<IResult> DeleteChannel(
        Guid id,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        Attic.Api.Hubs.ChannelEventBroadcaster events,
        CancellationToken ct)
```

- [ ] **Step 32.3: Build**

```bash
dotnet build src/Attic.Api
```

Expected: 0/0.

- [ ] **Step 32.4: Commit**

```bash
git add src/Attic.Api/Endpoints/ChannelsEndpoints.cs \
        src/Attic.Api/Endpoints/ChannelMembersEndpoints.cs \
        src/Attic.Api/Endpoints/InvitationsEndpoints.cs
git commit -m "feat(api): emit SignalR events from Phase 2 REST mutations"
```

---

## Task 33: `ChatHub.DeleteMessage` — author or room admin

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

- [ ] **Step 33.1: Add `DeleteMessage` method to `ChatHub`**

Insert after `UnsubscribeFromChannel`:

```csharp
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
```

- [ ] **Step 33.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs
git commit -m "feat(api): ChatHub.DeleteMessage (author or room admin)"
```

---

## Task 34: Tighten `ChatHub.SubscribeToChannel` — require membership

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

Phase 1 allowed any authenticated user to subscribe to any existing channel. Phase 2 requires the caller to be a non-banned member.

- [ ] **Step 34.1: Replace `SubscribeToChannel` body**

```csharp
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
```

- [ ] **Step 34.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs
git commit -m "fix(api): ChatHub.SubscribeToChannel requires active membership"
```

---

## Task 35: Realtime integration tests — RemovedFromChannel + MessageDeleted

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/RealtimeEventsFlowTests.cs`

- [ ] **Step 35.1: Write `RealtimeEventsFlowTests.cs`**

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
public sealed class RealtimeEventsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    private static async Task<HubConnection> ConnectHub(AppHostFixture fx, HttpClientHandler handler, CancellationToken ct)
    {
        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
        var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();
        await connection.StartAsync(ct);
        return connection;
    }

    [Fact]
    public async Task Banned_user_receives_RemovedFromChannel_over_hub()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"rm-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        // Joiner registers + joins, opens a hub connection and listens for RemovedFromChannel.
        var joinerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var joiner = new HttpClient(joinerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var email = $"rm-{Guid.NewGuid():N}@example.com";
        var username = $"j{Random.Shared.Next():x}";
        (await joiner.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        await using var connection = await ConnectHub(fx, joinerHandler, ct);
        var removed = new TaskCompletionSource<(Guid channelId, string reason)>();
        connection.On<Guid, string>("RemovedFromChannel", (cid, reason) => removed.TrySetResult((cid, reason)));

        // Owner bans the joiner.
        var list = await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct);
        var members = await list.Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct);
        var joinerId = members!.First(m => m.Username == username).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct)).EnsureSuccessStatusCode();

        var evt = await removed.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.channelId.ShouldBe(channel.Id);
        evt.reason.ShouldBe("banned");
    }

    [Fact]
    public async Task Admin_delete_message_fires_MessageDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"md-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (poster, _, _) = await Register(ct);
        (await poster.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var posterHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        // Swap handler via reflection is ugly — just reuse the already-configured poster client's handler.
        // Instead, build a new channel-subscribed connection for the OWNER (who will delete the message)
        // and a separate one for POSTER (who sends the message) and OWNER listens for MessageDeleted.

        var ownerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var ownerClient = new HttpClient(ownerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var ownerEmail = $"o-{Guid.NewGuid():N}@example.com";
        var ownerUser = $"o{Random.Shared.Next():x}";
        (await ownerClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(ownerEmail, ownerUser, "hunter2pw"), ct)).EnsureSuccessStatusCode();
        var altCreate = await ownerClient.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"md2-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var altChannel = (await altCreate.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        // Poster joins alt channel and sends via hub.
        var altPosterHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var altPoster = new HttpClient(altPosterHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await altPoster.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"p-{Guid.NewGuid():N}@example.com", $"p{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();
        (await altPoster.PostAsync($"/api/channels/{altChannel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        await using var ownerHub = await ConnectHub(fx, ownerHandler, ct);
        var deleted = new TaskCompletionSource<(Guid cid, long mid)>();
        ownerHub.On<Guid, long>("MessageDeleted", (cid, mid) => deleted.TrySetResult((cid, mid)));
        (await ownerHub.InvokeAsync<JsonElement>("SubscribeToChannel", altChannel.Id, ct))
            .GetProperty("ok").GetBoolean().ShouldBeTrue();

        await using var posterHub = await ConnectHub(fx, altPosterHandler, ct);
        (await posterHub.InvokeAsync<JsonElement>("SubscribeToChannel", altChannel.Id, ct))
            .GetProperty("ok").GetBoolean().ShouldBeTrue();

        var send = await posterHub.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(altChannel.Id, Guid.NewGuid(), "delete me", null), ct);
        send.Ok.ShouldBeTrue();
        var messageId = send.ServerId!.Value;

        var del = await ownerHub.InvokeAsync<JsonElement>("DeleteMessage", messageId, ct);
        del.GetProperty("ok").GetBoolean().ShouldBeTrue();

        var evt = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.cid.ShouldBe(altChannel.Id);
        evt.mid.ShouldBe(messageId);
    }
}
```

- [ ] **Step 35.2: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "RealtimeEventsFlowTests"
git add tests/Attic.Api.IntegrationTests/RealtimeEventsFlowTests.cs
git commit -m "test(api): realtime event delivery — RemovedFromChannel + MessageDeleted"
```

---

## Task 36: Full Checkpoint 3 test sweep

**Files:** none

- [ ] **Step 36.1: Run full suite**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: all Domain + Integration tests green. No regressions from the hub tightening (Phase 1 messaging tests adapted in Task 14 rely on membership now).

- [ ] **Step 36.2: Commit checkpoint marker**

```bash
git commit --allow-empty -m "chore: Phase 2 Checkpoint 3 (hub realtime) green"
```

---

## Task 37: Frontend — types + API clients

**Files:**
- Modify: `src/Attic.Web/src/types.ts`
- Create: `src/Attic.Web/src/api/channels.ts`
- Create: `src/Attic.Web/src/api/invitations.ts`

- [ ] **Step 37.1: Append to `src/Attic.Web/src/types.ts`**

```ts
export interface UserSummary {
  id: string;
  username: string;
}

export interface ChannelSummary {
  id: string;
  kind: 'public' | 'private' | 'personal';
  name: string | null;
  description: string | null;
  ownerId: string | null;
  memberCount: number;
  unreadCount: number;
}

export interface ChannelDetails {
  id: string;
  kind: 'public' | 'private' | 'personal';
  name: string | null;
  description: string | null;
  ownerId: string | null;
  createdAt: string;
  memberCount: number;
}

export interface CreateChannelRequest {
  name: string;
  description: string | null;
  kind: 'public' | 'private';
}

export interface UpdateChannelRequest {
  name: string | null;
  description: string | null;
}

export interface ChannelMemberSummary {
  userId: string;
  username: string;
  role: 'owner' | 'admin' | 'member';
  joinedAt: string;
}

export interface BannedMemberSummary {
  userId: string;
  username: string;
  bannedById: string;
  bannedByUsername: string | null;
  bannedAt: string;
  reason: string | null;
}

export interface ChangeRoleRequest {
  role: 'admin' | 'member';
}

export interface PublicCatalogItem {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
}

export interface InvitationDto {
  id: string;
  channelId: string;
  channelName: string;
  inviterId: string;
  inviterUsername: string;
  status: string;
  createdAt: string;
  decidedAt: string | null;
}

export interface InviteToChannelRequest {
  username: string;
}
```

- [ ] **Step 37.2: Write `src/Attic.Web/src/api/channels.ts`**

```ts
import { api } from './client';
import type {
  ChannelSummary, ChannelDetails, CreateChannelRequest, UpdateChannelRequest,
  ChannelMemberSummary, BannedMemberSummary, ChangeRoleRequest, PublicCatalogItem, PagedResult,
} from '../types';

export const channelsApi = {
  listMine: () => api.get<ChannelSummary[]>('/api/channels/mine'),
  getPublic: (search?: string, cursor?: string | null) => {
    const qs = new URLSearchParams();
    if (search) qs.set('search', search);
    if (cursor) qs.set('cursor', cursor);
    return api.get<PagedResult<PublicCatalogItem>>(`/api/channels/public?${qs}`);
  },
  getDetails: (id: string) => api.get<ChannelDetails>(`/api/channels/${id}`),
  create: (req: CreateChannelRequest) => api.post<ChannelDetails>('/api/channels', req),
  update: (id: string, req: UpdateChannelRequest) => api.patch<ChannelDetails>(`/api/channels/${id}`, req),
  delete: (id: string) => api.delete<void>(`/api/channels/${id}`),
  join: (id: string) => api.post<void>(`/api/channels/${id}/join`),
  leave: (id: string) => api.post<void>(`/api/channels/${id}/leave`),
  members: (id: string) => api.get<ChannelMemberSummary[]>(`/api/channels/${id}/members`),
  bans: (id: string) => api.get<BannedMemberSummary[]>(`/api/channels/${id}/bans`),
  banMember: (id: string, userId: string) => api.delete<void>(`/api/channels/${id}/members/${userId}`),
  changeRole: (id: string, userId: string, role: 'admin' | 'member') =>
    api.post<void>(`/api/channels/${id}/members/${userId}/role`, { role } as ChangeRoleRequest),
  unban: (id: string, userId: string) => api.delete<void>(`/api/channels/${id}/bans/${userId}`),
};
```

- [ ] **Step 37.3: Add `patch` + `delete` helpers to `src/Attic.Web/src/api/client.ts`**

Open `src/Attic.Web/src/api/client.ts`. Inside the exported `api` object, add after `post`:

```ts
  async patch<T>(path: string, body?: unknown): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), {
      method: 'PATCH',
      credentials: 'include',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    return handle<T>(r);
  },
  async delete<T>(path: string): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), { method: 'DELETE', credentials: 'include' });
    return handle<T>(r);
  },
```

- [ ] **Step 37.4: Write `src/Attic.Web/src/api/invitations.ts`**

```ts
import { api } from './client';
import type { InvitationDto, InviteToChannelRequest } from '../types';

export const invitationsApi = {
  listMine: () => api.get<InvitationDto[]>('/api/invitations'),
  issue: (channelId: string, req: InviteToChannelRequest) =>
    api.post<InvitationDto>(`/api/channels/${channelId}/invitations`, req),
  accept: (id: string) => api.post<void>(`/api/invitations/${id}/accept`),
  decline: (id: string) => api.post<void>(`/api/invitations/${id}/decline`),
};
```

- [ ] **Step 37.5: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/types.ts src/Attic.Web/src/api/channels.ts src/Attic.Web/src/api/invitations.ts src/Attic.Web/src/api/client.ts
git commit -m "feat(web): typed Phase 2 DTOs + channels/invitations API clients"
```

---

## Task 38: Frontend — extend SignalR wrapper with Phase 2 events

**Files:**
- Modify: `src/Attic.Web/src/api/signalr.ts`

- [ ] **Step 38.1: Add event registration helpers to the singleton**

Replace the exported `HubClient` interface + factory so it supports:

- `onChannelMemberJoined`, `onChannelMemberLeft`, `onChannelMemberRoleChanged`
- `onRemovedFromChannel`, `onChannelDeleted`, `onInvitationReceived`, `onMessageDeleted`

Each returns an unsubscribe function, mirroring `onMessageCreated`.

```ts
import * as signalR from '@microsoft/signalr';
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto } from '../types';

export interface HubClient {
  connection: signalR.HubConnection;
  subscribeToChannel(channelId: string): Promise<void>;
  unsubscribeFromChannel(channelId: string): Promise<void>;
  sendMessage(channelId: string, clientMessageId: string, content: string): Promise<SendMessageResponse>;
  deleteMessage(messageId: number): Promise<{ ok: boolean; error?: string }>;
  onMessageCreated(cb: (m: MessageDto) => void): () => void;
  onMessageDeleted(cb: (channelId: string, messageId: number) => void): () => void;
  onChannelMemberJoined(cb: (channelId: string, member: ChannelMemberSummary) => void): () => void;
  onChannelMemberLeft(cb: (channelId: string, userId: string) => void): () => void;
  onChannelMemberRoleChanged(cb: (channelId: string, userId: string, role: string) => void): () => void;
  onRemovedFromChannel(cb: (channelId: string, reason: string) => void): () => void;
  onChannelDeleted(cb: (channelId: string) => void): () => void;
  onInvitationReceived(cb: (invitation: InvitationDto) => void): () => void;
}

let singleton: HubClient | null = null;

export function getOrCreateHubClient(): HubClient {
  if (singleton) return singleton;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub', { withCredentials: true })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  let startPromise: Promise<void> | null = null;
  function ensureStarted() {
    if (connection.state === signalR.HubConnectionState.Connected) return Promise.resolve();
    if (!startPromise) startPromise = connection.start();
    return startPromise;
  }

  function on<Args extends unknown[]>(name: string, cb: (...args: Args) => void): () => void {
    const handler = (...args: Args) => cb(...args);
    connection.on(name, handler);
    return () => connection.off(name, handler);
  }

  singleton = {
    connection,
    async subscribeToChannel(channelId) {
      await ensureStarted();
      await connection.invoke('SubscribeToChannel', channelId);
    },
    async unsubscribeFromChannel(channelId) {
      if (connection.state !== signalR.HubConnectionState.Connected) return;
      await connection.invoke('UnsubscribeFromChannel', channelId);
    },
    async sendMessage(channelId, clientMessageId, content) {
      await ensureStarted();
      return connection.invoke<SendMessageResponse>('SendMessage', {
        channelId, clientMessageId, content, replyToId: null,
      });
    },
    async deleteMessage(messageId) {
      await ensureStarted();
      return connection.invoke<{ ok: boolean; error?: string }>('DeleteMessage', messageId);
    },
    onMessageCreated: (cb) => on<[MessageDto]>('MessageCreated', cb),
    onMessageDeleted: (cb) => on<[string, number]>('MessageDeleted', cb),
    onChannelMemberJoined: (cb) => on<[string, ChannelMemberSummary]>('ChannelMemberJoined', cb),
    onChannelMemberLeft: (cb) => on<[string, string]>('ChannelMemberLeft', cb),
    onChannelMemberRoleChanged: (cb) => on<[string, string, string]>('ChannelMemberRoleChanged', cb),
    onRemovedFromChannel: (cb) => on<[string, string]>('RemovedFromChannel', cb),
    onChannelDeleted: (cb) => on<[string]>('ChannelDeleted', cb),
    onInvitationReceived: (cb) => on<[InvitationDto]>('InvitationReceived', cb),
  };

  return singleton;
}

export function disposeHubClient() {
  if (singleton) {
    void singleton.connection.stop();
    singleton = null;
  }
}
```

- [ ] **Step 38.2: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/api/signalr.ts
git commit -m "feat(web): extend SignalR client with Phase 2 event handlers + DeleteMessage"
```

---

## Task 38b: Frontend — routing + channel-id in URL

**Files:**
- Modify: `src/Attic.Web/src/App.tsx`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`
- Modify: `src/Attic.Web/src/chat/ChatWindow.tsx`

- [ ] **Step 38b.1: Replace `App.tsx`**

```tsx
import { Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { AuthGate } from './auth/AuthGate';
import { Login } from './auth/Login';
import { Register } from './auth/Register';
import { ChatShell } from './chat/ChatShell';

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route element={<AuthGate />}>
          <Route path="/" element={<ChatShell />} />
          <Route path="/chat/:channelId" element={<ChatShell />} />
          <Route path="/catalog" element={<ChatShell />} />
          <Route path="/invitations" element={<ChatShell />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}
```

- [ ] **Step 38b.2: Replace `ChatWindow.tsx` to read channel id from route**

```tsx
import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { useDeleteMessage } from './useDeleteMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';

export function ChatWindow() {
  const { channelId } = useParams<{ channelId: string }>();
  const { user } = useAuth();

  if (!channelId) {
    return <div className="p-8 text-slate-500">Select a channel on the left to start chatting.</div>;
  }

  return <ChatWindowFor channelId={channelId} user={{ id: user!.id, username: user!.username }} />;
}

function ChatWindowFor({ channelId, user }: { channelId: string; user: { id: string; username: string } }) {
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(channelId);
  const send = useSendMessage(channelId, user);
  const del = useDeleteMessage(channelId);
  const [menuMsgId, setMenuMsgId] = useState<number | null>(null);

  const listRef = useRef<HTMLDivElement>(null);
  const lockedToBottom = useRef(true);

  useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (lockedToBottom.current) el.scrollTop = el.scrollHeight;
  }, [items.length]);

  function onScroll(e: React.UIEvent<HTMLDivElement>) {
    const el = e.currentTarget;
    lockedToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
    if (el.scrollTop === 0 && hasNextPage && !isFetchingNextPage) void fetchNextPage();
  }

  const ordered = [...items].reverse();

  return (
    <div className="flex flex-col h-full">
      <div ref={listRef} onScroll={onScroll} className="flex-1 overflow-y-auto p-4 space-y-2 bg-slate-50">
        {isFetchingNextPage && <div className="text-center text-xs text-slate-400">Loading older…</div>}
        {ordered.map(m => (
          <div key={m.id} className="bg-white rounded px-3 py-2 shadow-sm group relative">
            <div className="text-xs text-slate-500 flex justify-between">
              <span>
                {m.senderUsername} · {new Date(m.createdAt).toLocaleTimeString()}
                {m.updatedAt && <span className="ml-2 text-slate-400">(edited)</span>}
                {m.id < 0 && <span className="ml-2 text-slate-400">sending…</span>}
              </span>
              {m.id > 0 && (
                <button
                  onClick={() => setMenuMsgId(menuMsgId === m.id ? null : m.id)}
                  className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-slate-600 px-1"
                  aria-label="Message actions"
                >
                  ⋯
                </button>
              )}
            </div>
            <div className="whitespace-pre-wrap break-words">{m.content}</div>
            {menuMsgId === m.id && (
              <div className="absolute right-2 top-8 bg-white border rounded shadow z-10">
                <button
                  className="block w-full text-left px-3 py-1 text-sm hover:bg-slate-100 text-red-600"
                  onClick={() => { void del(m.id); setMenuMsgId(null); }}
                >
                  Delete
                </button>
              </div>
            )}
          </div>
        ))}
      </div>
      <ChatInput onSend={send} />
    </div>
  );
}
```

- [ ] **Step 38b.3: Write `src/Attic.Web/src/chat/useDeleteMessage.ts`**

```ts
import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

export function useDeleteMessage(channelId: string) {
  const qc = useQueryClient();
  return useCallback(async (messageId: number) => {
    const hub = getOrCreateHubClient();
    const ack = await hub.deleteMessage(messageId);
    if (!ack.ok) throw new Error(ack.error ?? 'delete_failed');
    qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(
      ['channel-messages', channelId],
      prev => {
        if (!prev) return prev;
        return {
          ...prev,
          pages: prev.pages.map(p => ({ ...p, items: p.items.filter(m => m.id !== messageId) })),
        };
      }
    );
  }, [channelId, qc]);
}
```

- [ ] **Step 38b.4: Defer ChatShell rewrite to Task 39**

The next task replaces `ChatShell.tsx` to host the sidebar. For now keep `ChatShell` as Phase 1's version — the routing still works because the current `ChatShell` shows only the chat window.

- [ ] **Step 38b.5: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/App.tsx src/Attic.Web/src/chat/ChatWindow.tsx src/Attic.Web/src/chat/useDeleteMessage.ts
git commit -m "feat(web): route-driven ChatWindow with delete-message hover menu"
```

---

## Task 39: Frontend — `Sidebar` with public/private tabs + `ChatShell` rewrite

**Files:**
- Create: `src/Attic.Web/src/chat/Sidebar.tsx`
- Create: `src/Attic.Web/src/chat/useChannelList.ts`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`

- [ ] **Step 39.1: Write `src/Attic.Web/src/chat/useChannelList.ts`**

```ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { channelsApi } from '../api/channels';
import { getOrCreateHubClient } from '../api/signalr';

export function useChannelList() {
  const qc = useQueryClient();
  const query = useQuery({
    queryKey: ['channels', 'mine'] as const,
    queryFn: () => channelsApi.listMine(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const offJoined = hub.onChannelMemberJoined(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offLeft = hub.onChannelMemberLeft(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offDeleted = hub.onChannelDeleted(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    const offRemoved = hub.onRemovedFromChannel(() => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); });
    return () => { offJoined(); offLeft(); offDeleted(); offRemoved(); };
  }, [qc]);

  return query;
}
```

- [ ] **Step 39.2: Write `src/Attic.Web/src/chat/Sidebar.tsx`**

```tsx
import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useChannelList } from './useChannelList';

type Tab = 'public' | 'private' | 'personal';

export function Sidebar({ onCreate }: { onCreate: () => void }) {
  const { data, isLoading } = useChannelList();
  const [tab, setTab] = useState<Tab>('public');
  const { pathname } = useLocation();

  const channels = (data ?? []).filter(c => c.kind === tab);

  return (
    <aside className="w-64 border-r bg-white flex flex-col">
      <nav className="flex border-b text-sm">
        {(['public', 'private', 'personal'] as const).map(k => (
          <button key={k} onClick={() => setTab(k)}
                  className={`flex-1 py-2 ${tab === k ? 'font-semibold border-b-2 border-blue-600' : 'text-slate-500'}`}>
            {k[0].toUpperCase() + k.slice(1)}
          </button>
        ))}
      </nav>
      <div className="p-2 border-b flex gap-2">
        <Link to="/catalog" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Catalog
        </Link>
        <button onClick={onCreate} className="flex-1 text-xs px-2 py-1 border rounded hover:bg-slate-50">
          + New room
        </button>
      </div>
      <ul className="flex-1 overflow-y-auto">
        {isLoading && <li className="p-3 text-slate-400 text-sm">Loading…</li>}
        {!isLoading && channels.length === 0 && (
          <li className="p-3 text-slate-400 text-sm">No {tab} channels.</li>
        )}
        {channels.map(c => {
          const href = `/chat/${c.id}`;
          const active = pathname === href;
          return (
            <li key={c.id}>
              <Link to={href}
                    className={`block px-3 py-2 text-sm truncate ${active ? 'bg-blue-50 text-blue-700' : 'hover:bg-slate-50'}`}>
                {c.name ?? 'Personal chat'}
                {c.unreadCount > 0 && (
                  <span className="ml-2 text-xs bg-blue-600 text-white rounded-full px-2">{c.unreadCount}</span>
                )}
              </Link>
            </li>
          );
        })}
      </ul>
      <div className="p-2 border-t">
        <Link to="/invitations" className="block text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Invitations
        </Link>
      </div>
    </aside>
  );
}
```

- [ ] **Step 39.3: Replace `src/Attic.Web/src/chat/ChatShell.tsx`**

```tsx
import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { ChatWindow } from './ChatWindow';
import { Sidebar } from './Sidebar';
import { CreateRoomModal } from './CreateRoomModal';
import { PublicCatalog } from './PublicCatalog';
import { RoomDetails } from './RoomDetails';
import { InvitationsInbox } from './InvitationsInbox';
import { disposeHubClient } from '../api/signalr';

export function ChatShell() {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { channelId } = useParams<{ channelId: string }>();
  const [createOpen, setCreateOpen] = useState(false);

  async function logout() {
    try { await api.post<void>('/api/auth/logout'); } catch { /* ignore */ }
    disposeHubClient();
    setUser(null);
    navigate('/login', { replace: true });
  }

  return (
    <div className="h-screen flex flex-col">
      <header className="flex items-center justify-between px-4 py-2 border-b bg-white">
        <div className="font-semibold">Attic</div>
        <div className="text-sm text-slate-600">
          {user?.username}
          <button onClick={logout} className="ml-4 text-blue-600">Sign out</button>
        </div>
      </header>
      <div className="flex-1 flex overflow-hidden">
        <Sidebar onCreate={() => setCreateOpen(true)} />
        <main className="flex-1 flex overflow-hidden">
          {pathname === '/catalog' && <PublicCatalog />}
          {pathname === '/invitations' && <InvitationsInbox />}
          {pathname !== '/catalog' && pathname !== '/invitations' && (
            <>
              <div className="flex-1 flex flex-col"><ChatWindow /></div>
              {channelId && <RoomDetails channelId={channelId} />}
            </>
          )}
        </main>
      </div>
      {createOpen && <CreateRoomModal onClose={() => setCreateOpen(false)} />}
    </div>
  );
}
```

- [ ] **Step 39.4: Stub out the four referenced components so the build doesn't break**

Create each of these as minimal placeholders; the following tasks flesh them out.

**`src/Attic.Web/src/chat/CreateRoomModal.tsx`:**

```tsx
export function CreateRoomModal({ onClose }: { onClose: () => void }) {
  return <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
    <div className="bg-white rounded p-4">Create modal — see Task 40.</div>
  </div>;
}
```

**`src/Attic.Web/src/chat/PublicCatalog.tsx`:**

```tsx
export function PublicCatalog() {
  return <div className="p-8 text-slate-500">Public catalog — see Task 41.</div>;
}
```

**`src/Attic.Web/src/chat/RoomDetails.tsx`:**

```tsx
export function RoomDetails(_: { channelId: string }) {
  return <aside className="w-64 border-l bg-white p-4 text-slate-500 text-sm">Room details — see Task 42.</aside>;
}
```

**`src/Attic.Web/src/chat/InvitationsInbox.tsx`:**

```tsx
export function InvitationsInbox() {
  return <div className="p-8 text-slate-500">Invitations — see Task 43.</div>;
}
```

- [ ] **Step 39.5: Verify + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build
cd - && git add src/Attic.Web/src/chat
git commit -m "feat(web): Sidebar with tabs + ChatShell rewrite (stub panels)"
```

---

## Task 40: Frontend — `CreateRoomModal`

**Files:**
- Modify: `src/Attic.Web/src/chat/CreateRoomModal.tsx`

- [ ] **Step 40.1: Replace `CreateRoomModal.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import type { ApiError } from '../types';

export function CreateRoomModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [kind, setKind] = useState<'public' | 'private'>('public');
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => channelsApi.create({ name: name.trim(), description: description.trim() || null, kind }),
    onSuccess: (channel) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${channel.id}`);
      onClose();
    },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Create failed'),
  });

  return (
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold">New room</h2>
        <input className="w-full border rounded px-3 py-2" placeholder="Name (3-120 chars)"
               value={name} onChange={e => setName(e.target.value)} maxLength={120} />
        <input className="w-full border rounded px-3 py-2" placeholder="Description (optional)"
               value={description} onChange={e => setDescription(e.target.value)} maxLength={1024} />
        <div className="flex gap-4 text-sm">
          <label className="flex items-center gap-2">
            <input type="radio" checked={kind === 'public'} onChange={() => setKind('public')} /> Public
          </label>
          <label className="flex items-center gap-2">
            <input type="radio" checked={kind === 'private'} onChange={() => setKind('private')} /> Private
          </label>
        </div>
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => mutation.mutate()}
                  disabled={mutation.isPending || name.trim().length < 3}
                  className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
            {mutation.isPending ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 40.2: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/chat/CreateRoomModal.tsx
git commit -m "feat(web): CreateRoomModal with kind selector"
```

---

## Task 41: Frontend — `PublicCatalog`

**Files:**
- Modify: `src/Attic.Web/src/chat/PublicCatalog.tsx`
- Create: `src/Attic.Web/src/chat/usePublicCatalog.ts`

- [ ] **Step 41.1: Write `usePublicCatalog.ts`**

```ts
import { useInfiniteQuery } from '@tanstack/react-query';
import { channelsApi } from '../api/channels';

export function usePublicCatalog(search: string) {
  return useInfiniteQuery({
    queryKey: ['channels', 'public', search] as const,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => channelsApi.getPublic(search || undefined, pageParam),
    getNextPageParam: (last) => last.nextCursor,
  });
}
```

- [ ] **Step 41.2: Replace `PublicCatalog.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import { usePublicCatalog } from './usePublicCatalog';

export function PublicCatalog() {
  const [search, setSearch] = useState('');
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = usePublicCatalog(search);
  const items = (data?.pages ?? []).flatMap(p => p.items);

  const join = useMutation({
    mutationFn: (id: string) => channelsApi.join(id),
    onSuccess: (_data, id) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${id}`);
    },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Public rooms</h1>
      <input className="w-full border rounded px-3 py-2 mb-4" placeholder="Search by name prefix…"
             value={search} onChange={e => setSearch(e.target.value)} />
      <ul className="divide-y bg-white rounded border">
        {items.map(c => (
          <li key={c.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{c.name}</div>
              <div className="text-sm text-slate-500">{c.description ?? '—'} · {c.memberCount} members</div>
            </div>
            <button onClick={() => join.mutate(c.id)}
                    disabled={join.isPending}
                    className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
              Join
            </button>
          </li>
        ))}
        {items.length === 0 && <li className="p-6 text-center text-slate-400">No rooms yet — create one.</li>}
      </ul>
      {hasNextPage && (
        <button onClick={() => fetchNextPage()} disabled={isFetchingNextPage}
                className="mt-4 text-sm text-blue-600 self-center">
          {isFetchingNextPage ? 'Loading…' : 'Load more'}
        </button>
      )}
    </div>
  );
}
```

- [ ] **Step 41.3: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/chat/PublicCatalog.tsx src/Attic.Web/src/chat/usePublicCatalog.ts
git commit -m "feat(web): PublicCatalog with search + join"
```

---

## Task 42: Frontend — `RoomDetails` (members + admin actions)

**Files:**
- Modify: `src/Attic.Web/src/chat/RoomDetails.tsx`
- Create: `src/Attic.Web/src/chat/useChannelDetails.ts`
- Create: `src/Attic.Web/src/chat/useChannelMembers.ts`

- [ ] **Step 42.1: Write `useChannelDetails.ts`**

```ts
import { useQuery } from '@tanstack/react-query';
import { channelsApi } from '../api/channels';

export function useChannelDetails(channelId: string) {
  return useQuery({
    queryKey: ['channel-details', channelId] as const,
    queryFn: () => channelsApi.getDetails(channelId),
    staleTime: 10_000,
  });
}
```

- [ ] **Step 42.2: Write `useChannelMembers.ts`**

```ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { channelsApi } from '../api/channels';
import { getOrCreateHubClient } from '../api/signalr';

export function useChannelMembers(channelId: string) {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['channel-members', channelId] as const,
    queryFn: () => channelsApi.members(channelId),
    staleTime: 10_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); };
    const off1 = hub.onChannelMemberJoined(invalidate);
    const off2 = hub.onChannelMemberLeft(invalidate);
    const off3 = hub.onChannelMemberRoleChanged(invalidate);
    return () => { off1(); off2(); off3(); };
  }, [channelId, qc]);

  return q;
}
```

- [ ] **Step 42.3: Replace `RoomDetails.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { channelsApi } from '../api/channels';
import { invitationsApi } from '../api/invitations';
import { useAuth } from '../auth/useAuth';
import { useChannelDetails } from './useChannelDetails';
import { useChannelMembers } from './useChannelMembers';

export function RoomDetails({ channelId }: { channelId: string }) {
  const { user } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const { data: details } = useChannelDetails(channelId);
  const { data: members } = useChannelMembers(channelId);

  const selfRole = members?.find(m => m.userId === user?.id)?.role;
  const canManage = selfRole === 'owner' || selfRole === 'admin';
  const isOwner = selfRole === 'owner';

  const leave = useMutation({
    mutationFn: () => channelsApi.leave(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const del = useMutation({
    mutationFn: () => channelsApi.delete(channelId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channels', 'mine'] }); navigate('/'); },
  });
  const ban = useMutation({
    mutationFn: (userId: string) => channelsApi.banMember(channelId, userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });
  const toggleRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: 'admin' | 'member' }) =>
      channelsApi.changeRole(channelId, userId, role),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['channel-members', channelId] }); },
  });

  const [inviteUsername, setInviteUsername] = useState('');
  const invite = useMutation({
    mutationFn: () => invitationsApi.issue(channelId, { username: inviteUsername.trim() }),
    onSuccess: () => setInviteUsername(''),
  });

  return (
    <aside className="w-72 border-l bg-white p-4 overflow-y-auto text-sm">
      <div className="mb-4">
        <div className="font-semibold text-base">{details?.name}</div>
        <div className="text-slate-500 text-xs">
          {details?.kind} · {details?.memberCount} members
        </div>
        {details?.description && <p className="text-slate-600 mt-2 text-xs">{details.description}</p>}
      </div>

      {details?.kind === 'private' && canManage && (
        <div className="mb-4 space-y-2">
          <div className="text-xs font-semibold text-slate-500 uppercase">Invite</div>
          <div className="flex gap-2">
            <input className="flex-1 border rounded px-2 py-1" placeholder="Username"
                   value={inviteUsername} onChange={e => setInviteUsername(e.target.value)} />
            <button onClick={() => invite.mutate()} disabled={invite.isPending || !inviteUsername.trim()}
                    className="px-2 py-1 text-xs bg-blue-600 text-white rounded disabled:opacity-50">
              Invite
            </button>
          </div>
        </div>
      )}

      <div className="mb-4">
        <div className="text-xs font-semibold text-slate-500 uppercase mb-1">Members</div>
        <ul className="space-y-1">
          {members?.map(m => (
            <li key={m.userId} className="flex items-center justify-between">
              <span>
                {m.username}
                <span className="ml-2 text-xs text-slate-400">{m.role}</span>
              </span>
              {canManage && m.userId !== user?.id && m.role !== 'owner' && (
                <div className="flex gap-1">
                  <button onClick={() => toggleRole.mutate({ userId: m.userId, role: m.role === 'admin' ? 'member' : 'admin' })}
                          className="text-xs text-blue-600">
                    {m.role === 'admin' ? 'Demote' : 'Promote'}
                  </button>
                  <button onClick={() => ban.mutate(m.userId)} className="text-xs text-red-600">Ban</button>
                </div>
              )}
            </li>
          ))}
        </ul>
      </div>

      <div className="border-t pt-3 space-y-2">
        {!isOwner && <button onClick={() => leave.mutate()} className="text-xs text-slate-600">Leave room</button>}
        {isOwner && <button onClick={() => del.mutate()} className="text-xs text-red-600">Delete room</button>}
      </div>
    </aside>
  );
}
```

- [ ] **Step 42.4: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/chat/RoomDetails.tsx src/Attic.Web/src/chat/useChannelDetails.ts src/Attic.Web/src/chat/useChannelMembers.ts
git commit -m "feat(web): RoomDetails with members list + admin actions + invite"
```

---

## Task 43: Frontend — `InvitationsInbox`

**Files:**
- Modify: `src/Attic.Web/src/chat/InvitationsInbox.tsx`
- Create: `src/Attic.Web/src/chat/useInvitations.ts`

- [ ] **Step 43.1: Write `useInvitations.ts`**

```ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { invitationsApi } from '../api/invitations';
import { getOrCreateHubClient } from '../api/signalr';

export function useInvitations() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['invitations'] as const,
    queryFn: () => invitationsApi.listMine(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off = hub.onInvitationReceived(() => { void qc.invalidateQueries({ queryKey: ['invitations'] }); });
    return () => { off(); };
  }, [qc]);

  return q;
}
```

- [ ] **Step 43.2: Replace `InvitationsInbox.tsx`**

```tsx
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { invitationsApi } from '../api/invitations';
import { useInvitations } from './useInvitations';

export function InvitationsInbox() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const { data, isLoading } = useInvitations();

  const accept = useMutation({
    mutationFn: (id: string) => invitationsApi.accept(id),
    onSuccess: (_data, _id) => {
      void qc.invalidateQueries({ queryKey: ['invitations'] });
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
    },
  });
  const decline = useMutation({
    mutationFn: (id: string) => invitationsApi.decline(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['invitations'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <h1 className="text-xl font-semibold mb-4">Invitations</h1>
      {isLoading && <div className="text-slate-500">Loading…</div>}
      {!isLoading && (data ?? []).length === 0 && (
        <div className="text-slate-400 bg-white border rounded p-6 text-center">No pending invitations.</div>
      )}
      <ul className="divide-y bg-white rounded border">
        {(data ?? []).map(inv => (
          <li key={inv.id} className="flex items-center justify-between px-4 py-2">
            <div>
              <div className="font-medium">{inv.channelName}</div>
              <div className="text-sm text-slate-500">
                Invited by {inv.inviterUsername} · {new Date(inv.createdAt).toLocaleString()}
              </div>
            </div>
            <div className="flex gap-2">
              <button onClick={() => { accept.mutate(inv.id); navigate(`/chat/${inv.channelId}`); }}
                      className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                Accept
              </button>
              <button onClick={() => decline.mutate(inv.id)} className="px-3 py-1 text-sm">
                Decline
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 43.3: Verify + commit**

```bash
cd src/Attic.Web && npm run lint
cd - && git add src/Attic.Web/src/chat/InvitationsInbox.tsx src/Attic.Web/src/chat/useInvitations.ts
git commit -m "feat(web): InvitationsInbox with accept/decline"
```

---

## Task 44: Frontend — `useChannelMessages` handles `MessageDeleted`; `useChannelList` handles `RemovedFromChannel`

**Files:**
- Modify: `src/Attic.Web/src/chat/useChannelMessages.ts`
- Create: `src/Attic.Web/src/chat/useRemovedFromChannel.ts`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`

- [ ] **Step 44.1: Extend `useChannelMessages.ts` with delete handling**

Open the file and replace the body of the `useEffect` with:

```ts
  useEffect(() => {
    const hub = getOrCreateHubClient();
    let active = true;
    void hub.subscribeToChannel(channelId);

    const offCreated = hub.onMessageCreated(msg => {
      if (!active || msg.channelId !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev || prev.pages.length === 0) {
          return { pages: [{ items: [msg], nextCursor: null }], pageParams: [null] };
        }
        const first = prev.pages[0];
        if (first.items.some(m => m.id === msg.id)) return prev;
        return { ...prev, pages: [{ ...first, items: [msg, ...first.items] }, ...prev.pages.slice(1)] };
      });
    });

    const offDeleted = hub.onMessageDeleted((cid, messageId) => {
      if (!active || cid !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        return {
          ...prev,
          pages: prev.pages.map(p => ({ ...p, items: p.items.filter(m => m.id !== messageId) })),
        };
      });
    });

    return () => {
      active = false;
      offCreated();
      offDeleted();
      void hub.unsubscribeFromChannel(channelId);
    };
  }, [channelId, qc, queryKey]);
```

- [ ] **Step 44.2: Write `useRemovedFromChannel.ts`**

```ts
import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { getOrCreateHubClient } from '../api/signalr';

// Listens for RemovedFromChannel and ChannelDeleted. If the affected channel is the
// currently-viewed one, navigates away to the root.
export function useRemovedFromChannel() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { channelId } = useParams<{ channelId: string }>();

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const off1 = hub.onRemovedFromChannel((cid, _reason) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      if (cid === channelId) navigate('/', { replace: true });
    });
    const off2 = hub.onChannelDeleted((cid) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      if (cid === channelId) navigate('/', { replace: true });
    });
    return () => { off1(); off2(); };
  }, [channelId, navigate, qc]);
}
```

- [ ] **Step 44.3: Mount `useRemovedFromChannel` inside `ChatShell`**

Open `src/Attic.Web/src/chat/ChatShell.tsx`. After `const { channelId } = useParams<...>();` add:

```tsx
  useRemovedFromChannel();
```

And at the top of the file: `import { useRemovedFromChannel } from './useRemovedFromChannel';`

- [ ] **Step 44.4: Verify + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build
cd - && git add src/Attic.Web/src/chat/useChannelMessages.ts src/Attic.Web/src/chat/useRemovedFromChannel.ts src/Attic.Web/src/chat/ChatShell.tsx
git commit -m "feat(web): realtime MessageDeleted + RemovedFromChannel/ChannelDeleted handling"
```

---

## Task 45: End-to-end smoke

**Files:** none (verification only)

- [ ] **Step 45.1: Full test sweep**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: all Domain + all Integration + MessagingFlowTests green (≈50+ tests total across the three test projects).

- [ ] **Step 45.2: Frontend build**

```bash
cd src/Attic.Web && npm run lint && npm run build
```

Expected: 0 errors, only pre-existing Rollup warnings from `@microsoft/signalr`.

- [ ] **Step 45.3: Aspire smoke**

Start the AppHost in the background:

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet run --project src/Attic.AppHost &
APPHOST_PID=$!
```

Wait for the API endpoint to respond (the port is discoverable via the Aspire dashboard at the URL logged to stdout; or probe the well-known dev port if you prefer). Verify:

- `GET /api/auth/me` → 401 (no cookie).
- `POST /api/auth/register` with fresh creds → 200 + cookie.
- `POST /api/channels` with the returned cookie → 200 + `ChannelDetails`.
- `GET /api/channels/mine` → list includes the new channel.

Kill the AppHost:

```bash
kill $APPHOST_PID
docker ps --format '{{.Names}}' | xargs -r docker rm -f
```

- [ ] **Step 45.4: Commit checkpoint marker**

```bash
git commit --allow-empty -m "chore: Phase 2 end-to-end smoke green"
```

---

## Phase 2 completion checklist

- [x] ChannelInvitation entity + InvitationStatus enum with full behavior tests
- [x] Seven new authorization rules in `AuthorizationRules` (Join, Leave, Manage, Delete, Ban, Change Role, Invite), all unit-tested
- [x] EF Core migration `AddChannelInvitations` with snake_case filtered unique index
- [x] Phase 1 hardcoded lobby + auto-join shortcut removed
- [x] REST: create / list-mine / details / update / delete / join / leave / members / ban / role / unban / invite / list-invitations / accept / decline (16 endpoints total)
- [x] Hub: tightened `SubscribeToChannel`, added `DeleteMessage`
- [x] `ChannelEventBroadcaster` emits `ChannelMemberJoined|Left|RoleChanged`, `RemovedFromChannel`, `ChannelDeleted`, `InvitationReceived`, `MessageDeleted`
- [x] Integration tests cover: CRUD, membership, moderation (ban/role/unban), invitations, realtime event delivery
- [x] FE: sidebar with tabs, create-room modal, public catalog, room details with admin actions, invitations inbox, route-driven channel switching, admin delete-message menu
- [x] FE: realtime cache invalidation for channel list, members list, invitations, messages

## What is deferred to later phases

- **Personal channels** — Phase 3 opens them via `POST /api/personal-chats/open` when the counterparty is a friend. The sidebar tab `Personal` is present but empty until then.
- **Friend-gated 1:1 messaging** — Phase 3 (friendship model + user-to-user blocks).
- **Attachments** — Phase 4 (upload/download/access control, reply, edit, delete-of-own-message via UI).
- **Presence heartbeats, active sessions, account deletion cascade** — Phase 5.
- **Rate limiting tuning, audit log surface, prod Docker image** — Phase 6.
