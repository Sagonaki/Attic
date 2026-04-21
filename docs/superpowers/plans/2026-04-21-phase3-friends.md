# Attic Phase 3 — Friends, Blocks, Personal Chats — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship friendships with bilateral acceptance, user-to-user blocks, and 1:1 personal chats gated by friendship + absence of blocks, with realtime fan-out over SignalR and a contacts / friend-request UI on the SPA.

**Architecture:** Adds three aggregate roots (`FriendRequest` with pending→accepted/declined/cancelled state machine; `Friendship` as a canonical unordered pair `(UserAId < UserBId)`; `UserBlock` as an asymmetric directed edge). Authorization rules in `Attic.Domain.Services.AuthorizationRules` (`CanSendFriendRequest`, `CanPostInPersonalChat`, `CanOpenPersonalChat`) stay pure and unit-tested. Personal chats reuse the Phase-2 `Channel` entity with `Kind = Personal`, two `ChannelMember` rows, `Name = null`, `OwnerId = null`. Blocking a user removes the `Friendship`, cancels pending `FriendRequest`s both ways, and keeps the `Channel` around as read-only (posts denied by the authorization rule, history still visible). SignalR events `FriendRequestReceived`, `FriendRequestDecided`, `FriendRemoved`, `Blocked` fan out via a small `FriendsEventBroadcaster` (`IHubContext<ChatHub>`). Frontend gets a `/contacts` route, a user-search-driven `SendFriendRequestModal`, personal-chat entries in the sidebar `Personal` tab, and realtime cache invalidation.

**Tech Stack:** Same as Phase 2 — .NET 10, Aspire 13.2.2, EF Core 10.0.5 + Npgsql, SignalR + Redis backplane, TanStack Query v5 + React Router v6 + `@microsoft/signalr` v8 + Tailwind 4. FluentValidation for request shape. xUnit v3 for tests.

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — Phase 3 boundary in §13, authorization in §8.1 (last three rows), REST surface in §9.1 (`Friends` group + `POST /api/personal-chats/open`), SignalR contract in §9.2 (friend + block events).

---

## Prerequisites — invariants from Phases 1 & 2

Do not regress any of these:

- **DbContext registration** uses `AddDbContext<AtticDbContext>` + `EnrichNpgsqlDbContext<AtticDbContext>()` (not `AddNpgsqlDbContext<T>`). Interceptor attached in the options callback.
- **Cookie / session crypto** lives in `Attic.Domain.Services.SessionToken` (pure, unit-tested). `Attic.Api.Auth.SessionFactory` composes it with `IClock`.
- **Hub methods** read user id via `Context.User` (SignalR invocation scopes don't populate the scoped `CurrentUser`). Use `private Guid? UserId => CurrentUser.ReadUserId(Context.User!);`.
- **Cookie `Secure` flag** derived from `HttpRequest.IsHttps`.
- **Raw SQL** in EF configurations uses snake_case identifiers — `UseSnakeCaseNamingConvention()` does not rewrite strings passed to `HasCheckConstraint` / `HasFilter`.
- **`TimestampInterceptor`** respects `IsModified` — domain methods that already set `UpdatedAt` own that semantic; others rely on the interceptor.
- **Entity `UpdatedAt` properties** are `{ get; private set; }`. Mutation is via domain methods only.
- **Authorization rules** are pure functions in `Attic.Domain.Services.AuthorizationRules`. Controllers and hub methods load the minimal rows, then call the rule; they do not inline rule logic.
- **REST mutations** that have realtime side effects go through a `*EventBroadcaster` injected as a scoped service, not direct `IHubContext` use in endpoint handlers. Phase 2 established this with `ChannelEventBroadcaster`; Phase 3 adds `FriendsEventBroadcaster`.
- **Aspire** is 13.2.2, package name is `Aspire.Hosting.JavaScript`, AppHost uses `AddViteApp("web", "../Attic.Web")`. Consult `~/.claude/skills/aspire/SKILL.md` first when in doubt.

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-3` (branched from merged `main` after Phase 2).
- `dotnet test tests/Attic.Domain.Tests` → 78 passing.
- `dotnet test tests/Attic.Api.IntegrationTests` → 33 passing.
- `cd src/Attic.Web && npm run lint && npm run build` exits 0.
- Podman running; `DOCKER_HOST` points at the podman socket.

---

## File structure additions

```
src/
├── Attic.Domain/
│   ├── Entities/
│   │   ├── FriendRequest.cs                                   (new)
│   │   ├── Friendship.cs                                      (new)
│   │   └── UserBlock.cs                                       (new)
│   ├── Enums/
│   │   └── FriendRequestStatus.cs                             (new)
│   └── Services/
│       ├── AuthorizationResult.cs                             (modify — add AlreadyFriends code)
│       └── AuthorizationRules.cs                              (modify — 3 new rule methods)
├── Attic.Infrastructure/
│   └── Persistence/
│       ├── AtticDbContext.cs                                  (modify — 3 DbSets)
│       ├── Configurations/
│       │   ├── FriendRequestConfiguration.cs                  (new)
│       │   ├── FriendshipConfiguration.cs                     (new)
│       │   └── UserBlockConfiguration.cs                      (new)
│       └── Migrations/
│           └── XXXXXXXXXXXXXX_AddFriendsAndBlocks.cs          (generated)
├── Attic.Contracts/
│   └── Friends/
│       ├── FriendRequestDto.cs                                (new)
│       ├── FriendDto.cs                                       (new — wraps UserSummary + createdAt)
│       ├── SendFriendRequestRequest.cs                        (new)
│       ├── UserSearchResult.cs                                (new)
│       └── OpenPersonalChatRequest.cs                         (new)
├── Attic.Api/
│   ├── Endpoints/
│   │   ├── FriendsEndpoints.cs                                (new)
│   │   ├── FriendRequestsEndpoints.cs                         (new)
│   │   ├── UsersEndpoints.cs                                  (new — GET /api/users/search, block/unblock)
│   │   ├── PersonalChatsEndpoints.cs                          (new)
│   │   └── ChannelsEndpoints.cs                               (unchanged from Phase 2)
│   ├── Hubs/
│   │   ├── FriendsEventBroadcaster.cs                         (new)
│   │   └── ChatHub.cs                                         (modify — CanPostInPersonalChat gate)
│   ├── Validators/
│   │   ├── SendFriendRequestRequestValidator.cs               (new)
│   │   └── OpenPersonalChatRequestValidator.cs                (new)
│   └── Program.cs                                             (modify — map 4 new endpoint groups + register broadcaster)
└── Attic.Web/
    └── src/
        ├── api/
        │   ├── friends.ts                                     (new)
        │   ├── users.ts                                       (new)
        │   ├── personalChats.ts                               (new)
        │   └── signalr.ts                                     (modify — 4 new event handlers)
        ├── chat/
        │   ├── Contacts.tsx                                   (new — /contacts page)
        │   ├── SendFriendRequestModal.tsx                     (new)
        │   ├── useFriends.ts                                  (new)
        │   ├── useFriendRequests.ts                           (new)
        │   ├── useUserSearch.ts                               (new)
        │   └── useOpenPersonalChat.ts                         (new)
        ├── App.tsx                                            (modify — /contacts route)
        ├── chat/ChatShell.tsx                                 (modify — render Contacts on /contacts)
        ├── chat/Sidebar.tsx                                   (modify — Personal tab shows personal channels + "New personal chat" button)
        └── types.ts                                           (modify — new DTO types)
tests/
├── Attic.Domain.Tests/
│   ├── AuthorizationRulesTests.cs                             (modify — 3 new rule test regions)
│   ├── FriendRequestTests.cs                                  (new)
│   ├── FriendshipTests.cs                                     (new — canonical ordering)
│   └── UserBlockTests.cs                                      (new)
└── Attic.Api.IntegrationTests/
    ├── FriendsFlowTests.cs                                    (new — request/accept/decline/remove)
    ├── BlocksFlowTests.cs                                     (new — block cancels friendship + pending requests)
    ├── PersonalChatFlowTests.cs                               (new — open + post when friends, deny when blocked)
    └── RealtimeFriendEventsTests.cs                           (new — FriendRequestReceived, Blocked)
```

Total: ~32 new files, ~10 modified. Central package manifest unchanged — no new third-party dependencies.

---

## Task ordering rationale

TDD bottom-up: domain entities first, then authorization rules, then EF mapping + migration, then REST endpoints with integration tests, then realtime wiring, then frontend. Each numbered task is a single commit; commit-message conventions from Phase 2 carry over (`feat(domain)`, `feat(infra)`, `feat(api)`, `feat(web)`, `test(api)`, `chore:`, `fix:`).

Four checkpoints map onto the review cycle:

- **Checkpoint 1 — Domain + Infra (Tasks 1-12):** enums, three entities with behavior tests, three authorization rules with unit tests, three EF configurations, migration.
- **Checkpoint 2 — Contracts + REST (Tasks 13-26):** DTOs, validators, 10 REST endpoints, integration tests for each flow.
- **Checkpoint 3 — Hub realtime (Tasks 27-30):** `FriendsEventBroadcaster`, wire broadcasts into REST endpoints, `ChatHub.SendMessage` personal-chat authorization, realtime integration tests.
- **Checkpoint 4 — Frontend (Tasks 31-38):** types, API clients, SignalR wrapper extension, contacts page, friend-request modal + user search, personal-chat creation flow, sidebar integration, end-to-end smoke.

---

## Task 1: `FriendRequestStatus` enum

**Files:**
- Create: `src/Attic.Domain/Enums/FriendRequestStatus.cs`

- [x] **Step 1.1: Write `FriendRequestStatus.cs`**

```csharp
namespace Attic.Domain.Enums;

public enum FriendRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3
}
```

- [x] **Step 1.2: Build + commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Enums/FriendRequestStatus.cs docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add FriendRequestStatus enum"
```

Expected: 0/0.

---

## Task 2: `FriendRequest` entity with unit tests (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/FriendRequest.cs`
- Create: `tests/Attic.Domain.Tests/FriendRequestTests.cs`

- [x] **Step 2.1: Write the failing tests — `tests/Attic.Domain.Tests/FriendRequestTests.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class FriendRequestTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Issue_creates_pending_request()
    {
        var req = FriendRequest.Issue(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            senderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            recipientId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            text: "hello",
            now: T0);

        req.Status.ShouldBe(FriendRequestStatus.Pending);
        req.Text.ShouldBe("hello");
        req.CreatedAt.ShouldBe(T0);
        req.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_self_request()
    {
        var self = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var act = () => FriendRequest.Issue(Guid.NewGuid(), senderId: self, recipientId: self, text: null, now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("recipientId");
    }

    [Fact]
    public void Issue_trims_and_nulls_empty_text()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text: "   ", now: T0);
        req.Text.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_text_over_500_chars()
    {
        var act = () => FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            text: new string('x', 501), now: T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("text");
    }

    [Fact]
    public void Accept_marks_accepted_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Accept(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Accepted);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Decline_marks_declined_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Decline(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Declined);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Cancel_marks_cancelled_and_stamps_decidedAt()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Cancel(T0.AddMinutes(5));
        req.Status.ShouldBe(FriendRequestStatus.Cancelled);
        req.DecidedAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    public void Accept_rejects_non_pending()
    {
        var req = FriendRequest.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, T0);
        req.Accept(T0.AddMinutes(5));
        Should.Throw<InvalidOperationException>(() => req.Accept(T0.AddMinutes(10)));
    }
}
```

- [x] **Step 2.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "FriendRequestTests"
```

Expected: compile failure — `FriendRequest` does not exist.

- [x] **Step 2.3: Implement `src/Attic.Domain/Entities/FriendRequest.cs`**

```csharp
using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class FriendRequest
{
    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid RecipientId { get; private set; }
    public string? Text { get; private set; }
    public FriendRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private FriendRequest() { }

    public static FriendRequest Issue(Guid id, Guid senderId, Guid recipientId, string? text, DateTimeOffset now)
    {
        if (senderId == recipientId)
            throw new ArgumentException("Cannot send a friend request to yourself.", nameof(recipientId));

        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmed is not null && trimmed.Length > 500)
            throw new ArgumentException("Text is limited to 500 characters.", nameof(text));

        return new FriendRequest
        {
            Id = id,
            SenderId = senderId,
            RecipientId = recipientId,
            Text = trimmed,
            Status = FriendRequestStatus.Pending,
            CreatedAt = now
        };
    }

    public void Accept(DateTimeOffset at) => Decide(FriendRequestStatus.Accepted, at);
    public void Decline(DateTimeOffset at) => Decide(FriendRequestStatus.Declined, at);
    public void Cancel(DateTimeOffset at) => Decide(FriendRequestStatus.Cancelled, at);

    private void Decide(FriendRequestStatus next, DateTimeOffset at)
    {
        if (Status != FriendRequestStatus.Pending)
            throw new InvalidOperationException($"Friend request is already {Status}.");
        Status = next;
        DecidedAt = at;
    }
}
```

- [x] **Step 2.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "FriendRequestTests"
```

Expected: 8 passing.

- [x] **Step 2.5: Commit**

```bash
git add src/Attic.Domain/Entities/FriendRequest.cs \
        tests/Attic.Domain.Tests/FriendRequestTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add FriendRequest entity with state-machine behavior"
```

---

## Task 3: `Friendship` entity with canonical unordered-pair tests (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/Friendship.cs`
- Create: `tests/Attic.Domain.Tests/FriendshipTests.cs`

A `Friendship` is an unordered relationship between two users. We store the canonical form by always putting the smaller `Guid` in `UserAId` and the larger in `UserBId`. The static factory `Create(a, b, now)` does the ordering so callers don't have to.

- [x] **Step 3.1: Write the failing tests**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class FriendshipTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_orders_ids_canonically()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f1 = Friendship.Create(a, b, T0);
        f1.UserAId.ShouldBe(a);
        f1.UserBId.ShouldBe(b);

        var f2 = Friendship.Create(b, a, T0);
        f2.UserAId.ShouldBe(a);
        f2.UserBId.ShouldBe(b);
    }

    [Fact]
    public void Create_rejects_identical_ids()
    {
        var u = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var act = () => Friendship.Create(u, u, T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("userBId");
    }

    [Fact]
    public void Involves_returns_true_for_either_participant()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f = Friendship.Create(a, b, T0);
        f.Involves(a).ShouldBeTrue();
        f.Involves(b).ShouldBeTrue();
        f.Involves(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void Other_returns_the_other_participant()
    {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var f = Friendship.Create(a, b, T0);
        f.Other(a).ShouldBe(b);
        f.Other(b).ShouldBe(a);
    }

    [Fact]
    public void Other_rejects_non_participant()
    {
        var f = Friendship.Create(Guid.NewGuid(), Guid.NewGuid(), T0);
        Should.Throw<ArgumentException>(() => f.Other(Guid.NewGuid()));
    }
}
```

- [x] **Step 3.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "FriendshipTests"
```

- [x] **Step 3.3: Implement `src/Attic.Domain/Entities/Friendship.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class Friendship
{
    public Guid UserAId { get; private set; }
    public Guid UserBId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Friendship() { }

    public static Friendship Create(Guid userAId, Guid userBId, DateTimeOffset now)
    {
        if (userAId == userBId)
            throw new ArgumentException("Friendship requires two distinct users.", nameof(userBId));

        var (smaller, larger) = userAId.CompareTo(userBId) < 0 ? (userAId, userBId) : (userBId, userAId);
        return new Friendship { UserAId = smaller, UserBId = larger, CreatedAt = now };
    }

    public bool Involves(Guid userId) => userId == UserAId || userId == UserBId;

    public Guid Other(Guid userId)
    {
        if (userId == UserAId) return UserBId;
        if (userId == UserBId) return UserAId;
        throw new ArgumentException("User is not part of this friendship.", nameof(userId));
    }
}
```

- [x] **Step 3.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "FriendshipTests"
```

Expected: 5 passing.

- [x] **Step 3.5: Commit**

```bash
git add src/Attic.Domain/Entities/Friendship.cs \
        tests/Attic.Domain.Tests/FriendshipTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add Friendship entity with canonical unordered-pair ordering"
```

---

## Task 4: `UserBlock` entity with unit tests (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/UserBlock.cs`
- Create: `tests/Attic.Domain.Tests/UserBlockTests.cs`

- [x] **Step 4.1: Write the failing tests**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class UserBlockTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_records_blocker_blocked_pair()
    {
        var blocker = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var blocked = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var b = UserBlock.Create(blocker, blocked, T0);
        b.BlockerId.ShouldBe(blocker);
        b.BlockedId.ShouldBe(blocked);
        b.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Create_rejects_self_block()
    {
        var u = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var act = () => UserBlock.Create(u, u, T0);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("blockedId");
    }
}
```

- [x] **Step 4.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "UserBlockTests"
```

- [x] **Step 4.3: Implement `src/Attic.Domain/Entities/UserBlock.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class UserBlock
{
    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private UserBlock() { }

    public static UserBlock Create(Guid blockerId, Guid blockedId, DateTimeOffset now)
    {
        if (blockerId == blockedId)
            throw new ArgumentException("Cannot block yourself.", nameof(blockedId));

        return new UserBlock { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = now };
    }
}
```

- [x] **Step 4.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "UserBlockTests"
```

Expected: 2 passing.

- [x] **Step 4.5: Commit**

```bash
git add src/Attic.Domain/Entities/UserBlock.cs \
        tests/Attic.Domain.Tests/UserBlockTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add UserBlock entity (asymmetric block edge)"
```

---

## Task 5: Extend `AuthorizationFailureReason` with `AlreadyFriends`

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationResult.cs`

- [x] **Step 5.1: Append `AlreadyFriends` to the enum (before `DuplicateFriendRequest`)**

Open `src/Attic.Domain/Services/AuthorizationResult.cs`. Insert `AlreadyFriends,` immediately before `DuplicateFriendRequest` so the enum reads:

```csharp
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
    AlreadyFriends,
    DuplicateFriendRequest
}
```

- [x] **Step 5.2: Build + commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Services/AuthorizationResult.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add AlreadyFriends authorization failure reason"
```

Expected: 0/0.

---

## Task 6: Authorization — `CanSendFriendRequest` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

The rule takes the minimal state: is there already a friendship? is there a pending request in either direction? is there a block in either direction?

- [x] **Step 6.1: Append the failing tests**

After the existing `CanInviteToChannel_*` tests in `AuthorizationRulesTests.cs`, append:

```csharp
    [Fact]
    public void CanSendFriendRequest_allows_when_no_relationship()
    {
        AuthorizationRules.CanSendFriendRequest(
            existingFriendship: false,
            hasPendingRequest: false,
            hasBlockInEitherDirection: false).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanSendFriendRequest_denies_already_friends()
    {
        AuthorizationRules.CanSendFriendRequest(
            existingFriendship: true,
            hasPendingRequest: false,
            hasBlockInEitherDirection: false).Reason
            .ShouldBe(AuthorizationFailureReason.AlreadyFriends);
    }

    [Fact]
    public void CanSendFriendRequest_denies_duplicate_pending()
    {
        AuthorizationRules.CanSendFriendRequest(
            existingFriendship: false,
            hasPendingRequest: true,
            hasBlockInEitherDirection: false).Reason
            .ShouldBe(AuthorizationFailureReason.DuplicateFriendRequest);
    }

    [Fact]
    public void CanSendFriendRequest_denies_when_blocked_in_either_direction()
    {
        AuthorizationRules.CanSendFriendRequest(
            existingFriendship: false,
            hasPendingRequest: false,
            hasBlockInEitherDirection: true).Reason
            .ShouldBe(AuthorizationFailureReason.BlockedByOrBlockingUser);
    }
```

- [x] **Step 6.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanSendFriendRequest"
```

- [x] **Step 6.3: Append the method to `AuthorizationRules.cs`** (after the last method, before the closing `}`)

```csharp
    public static AuthorizationResult CanSendFriendRequest(
        bool existingFriendship,
        bool hasPendingRequest,
        bool hasBlockInEitherDirection)
    {
        if (hasBlockInEitherDirection) return AuthorizationResult.Deny(AuthorizationFailureReason.BlockedByOrBlockingUser);
        if (existingFriendship) return AuthorizationResult.Deny(AuthorizationFailureReason.AlreadyFriends);
        if (hasPendingRequest) return AuthorizationResult.Deny(AuthorizationFailureReason.DuplicateFriendRequest);
        return AuthorizationResult.Ok();
    }
```

- [x] **Step 6.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: previous tests + 4 new = ~92 passing (depending on prior counts).

- [x] **Step 6.5: Commit**

```bash
git add src/Attic.Domain/Services/AuthorizationRules.cs \
        tests/Attic.Domain.Tests/AuthorizationRulesTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add CanSendFriendRequest rule"
```

---

## Task 7: Authorization — `CanPostInPersonalChat` + `CanOpenPersonalChat` (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

Posting in a personal chat requires a live friendship AND no block in either direction. Opening a personal chat has the same preconditions. They are the same rule — implement one, `CanOpenPersonalChat` delegates to it.

- [x] **Step 7.1: Append failing tests**

```csharp
    [Fact]
    public void CanPostInPersonalChat_allows_when_friends_and_no_block()
    {
        AuthorizationRules.CanPostInPersonalChat(
            areFriends: true,
            hasBlockInEitherDirection: false).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanPostInPersonalChat_denies_when_not_friends()
    {
        AuthorizationRules.CanPostInPersonalChat(
            areFriends: false,
            hasBlockInEitherDirection: false).Reason
            .ShouldBe(AuthorizationFailureReason.NotFriends);
    }

    [Fact]
    public void CanPostInPersonalChat_denies_when_blocked()
    {
        AuthorizationRules.CanPostInPersonalChat(
            areFriends: true,
            hasBlockInEitherDirection: true).Reason
            .ShouldBe(AuthorizationFailureReason.BlockedByOrBlockingUser);
    }

    [Fact]
    public void CanOpenPersonalChat_delegates_to_CanPostInPersonalChat()
    {
        AuthorizationRules.CanOpenPersonalChat(true, false).Allowed.ShouldBeTrue();
        AuthorizationRules.CanOpenPersonalChat(false, false).Reason.ShouldBe(AuthorizationFailureReason.NotFriends);
        AuthorizationRules.CanOpenPersonalChat(true, true).Reason.ShouldBe(AuthorizationFailureReason.BlockedByOrBlockingUser);
    }
```

- [x] **Step 7.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanPostInPersonalChat|CanOpenPersonalChat"
```

- [x] **Step 7.3: Append methods**

```csharp
    public static AuthorizationResult CanPostInPersonalChat(bool areFriends, bool hasBlockInEitherDirection)
    {
        if (hasBlockInEitherDirection) return AuthorizationResult.Deny(AuthorizationFailureReason.BlockedByOrBlockingUser);
        if (!areFriends) return AuthorizationResult.Deny(AuthorizationFailureReason.NotFriends);
        return AuthorizationResult.Ok();
    }

    public static AuthorizationResult CanOpenPersonalChat(bool areFriends, bool hasBlockInEitherDirection)
        => CanPostInPersonalChat(areFriends, hasBlockInEitherDirection);
```

- [x] **Step 7.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs \
        tests/Attic.Domain.Tests/AuthorizationRulesTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(domain): add CanPostInPersonalChat and CanOpenPersonalChat rules"
```

---

## Task 8: EF Core — `FriendRequestConfiguration`

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/FriendRequestConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [x] **Step 8.1: Write `FriendRequestConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> b)
    {
        b.ToTable("friend_requests");
        b.HasKey(r => r.Id);
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.Text).HasMaxLength(500);

        b.HasIndex(r => new { r.RecipientId, r.Status })
            .HasDatabaseName("ix_friend_requests_recipient_status");

        // Unique pending request in either direction between two users — we store sender/recipient
        // so the pending guard is one index; CanSendFriendRequest checks both directions.
        b.HasIndex(r => new { r.SenderId, r.RecipientId })
            .IsUnique()
            .HasDatabaseName("ux_friend_requests_sender_recipient_pending")
            .HasFilter($"status = {(int)FriendRequestStatus.Pending}");
    }
}
```

- [x] **Step 8.2: Add `DbSet<FriendRequest>` to `AtticDbContext.cs`**

Insert after `DbSet<ChannelInvitation>`:

```csharp
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
```

- [x] **Step 8.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/FriendRequestConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(infra): add FriendRequest EF Core configuration"
```

Expected: 0/0.

---

## Task 9: EF Core — `FriendshipConfiguration`

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/FriendshipConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [x] **Step 9.1: Write `FriendshipConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> b)
    {
        b.ToTable("friendships");
        // Canonical composite key — UserA is always the smaller GUID (enforced by Friendship.Create).
        b.HasKey(f => new { f.UserAId, f.UserBId });

        b.HasCheckConstraint("ck_friendships_user_order", "user_a_id < user_b_id");

        // Reverse-lookup index for "list my friends" where UserA = me OR UserB = me.
        // Postgres uses bitmap OR'd index scans across two single-col indexes efficiently,
        // so two indexes are simpler than a compound covering-index rewrite.
        b.HasIndex(f => f.UserAId).HasDatabaseName("ix_friendships_user_a");
        b.HasIndex(f => f.UserBId).HasDatabaseName("ix_friendships_user_b");
    }
}
```

- [x] **Step 9.2: Add `DbSet<Friendship>` to `AtticDbContext.cs`**

```csharp
    public DbSet<Friendship> Friendships => Set<Friendship>();
```

- [x] **Step 9.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/FriendshipConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(infra): add Friendship EF Core configuration"
```

---

## Task 10: EF Core — `UserBlockConfiguration`

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/UserBlockConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [ ] **Step 10.1: Write `UserBlockConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> b)
    {
        b.ToTable("user_blocks");
        b.HasKey(x => new { x.BlockerId, x.BlockedId });

        // "Is anyone blocking me?" lookup (the CanPostInPersonalChat gate).
        b.HasIndex(x => x.BlockedId).HasDatabaseName("ix_user_blocks_blocked");
    }
}
```

- [ ] **Step 10.2: Add `DbSet<UserBlock>` to `AtticDbContext.cs`**

```csharp
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
```

- [ ] **Step 10.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/UserBlockConfiguration.cs \
        src/Attic.Infrastructure/Persistence/AtticDbContext.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(infra): add UserBlock EF Core configuration"
```

---

## Task 11: EF Core migration — `AddFriendsAndBlocks`

**Files:**
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/*_AddFriendsAndBlocks.cs`
- Updated: `src/Attic.Infrastructure/Persistence/Migrations/AtticDbContextModelSnapshot.cs`

- [ ] **Step 11.1: Generate**

```bash
dotnet tool run dotnet-ef migrations add AddFriendsAndBlocks \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 11.2: Sanity-check the idempotent script**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase3-friends.sql
grep -i "friend_requests\|friendships\|user_blocks\|status = " /tmp/phase3-friends.sql | head -25
```

Must contain:
- `CREATE TABLE friend_requests (id uuid, sender_id uuid, recipient_id uuid, text character varying(500), status integer, created_at timestamp with time zone, decided_at timestamp with time zone, updated_at timestamp with time zone, …)`
- `CREATE INDEX ix_friend_requests_recipient_status ON friend_requests (recipient_id, status);`
- `CREATE UNIQUE INDEX ux_friend_requests_sender_recipient_pending ON friend_requests (sender_id, recipient_id) WHERE status = 0;` — unquoted `status`.
- `CREATE TABLE friendships (user_a_id uuid, user_b_id uuid, created_at timestamp with time zone, … CHECK (user_a_id < user_b_id))`.
- `CREATE INDEX ix_friendships_user_a` + `ix_friendships_user_b`.
- `CREATE TABLE user_blocks (blocker_id uuid, blocked_id uuid, created_at timestamp with time zone, …)`.
- `CREATE INDEX ix_user_blocks_blocked ON user_blocks (blocked_id);`.

If any filter clause reads `WHERE "Status" = 0` or any column appears quoted in a raw SQL string — STOP, that's the snake-case-raw-SQL bug. Fix the configuration (quote-free `status` in `HasFilter`) and regenerate.

- [ ] **Step 11.3: Build + commit**

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Migrations \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(infra): migration AddFriendsAndBlocks"
```

Expected: 0/0.

---

## Task 12: Checkpoint 1 test sweep

**Files:** none (verification only)

- [ ] **Step 12.1: Run full domain tests**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: previous 78 + 8 FriendRequest + 5 Friendship + 2 UserBlock + 7 authorization (4 CanSendFriendRequest + 3 CanPostInPersonalChat + 1 CanOpenPersonalChat counted separately in tests) ≈ 100 passing. Exact count may vary; zero failures is the bar.

- [ ] **Step 12.2: Commit checkpoint marker**

```bash
git commit --allow-empty -m "chore: Phase 3 Checkpoint 1 (domain + infra) green"
```

---

## Task 13: Contracts — Phase 3 DTOs

**Files:**
- Create: `src/Attic.Contracts/Friends/FriendRequestDto.cs`
- Create: `src/Attic.Contracts/Friends/FriendDto.cs`
- Create: `src/Attic.Contracts/Friends/SendFriendRequestRequest.cs`
- Create: `src/Attic.Contracts/Friends/UserSearchResult.cs`
- Create: `src/Attic.Contracts/Friends/OpenPersonalChatRequest.cs`

All `sealed record`.

- [ ] **Step 13.1: `Friends/FriendRequestDto.cs`**

```csharp
namespace Attic.Contracts.Friends;

public sealed record FriendRequestDto(
    Guid Id,
    Guid SenderId,
    string SenderUsername,
    Guid RecipientId,
    string RecipientUsername,
    string? Text,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
```

- [ ] **Step 13.2: `Friends/FriendDto.cs`**

```csharp
namespace Attic.Contracts.Friends;

public sealed record FriendDto(Guid UserId, string Username, DateTimeOffset FriendsSince);
```

- [ ] **Step 13.3: `Friends/SendFriendRequestRequest.cs`**

```csharp
namespace Attic.Contracts.Friends;

public sealed record SendFriendRequestRequest(string Username, string? Text);
```

- [ ] **Step 13.4: `Friends/UserSearchResult.cs`**

```csharp
namespace Attic.Contracts.Friends;

public sealed record UserSearchResult(Guid Id, string Username);
```

- [ ] **Step 13.5: `Friends/OpenPersonalChatRequest.cs`**

```csharp
namespace Attic.Contracts.Friends;

public sealed record OpenPersonalChatRequest(string Username);
```

- [ ] **Step 13.6: Build + commit**

```bash
dotnet build src/Attic.Contracts
git add src/Attic.Contracts/Friends \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(contracts): add Phase 3 friend + personal-chat DTOs"
```

---

## Task 14: Validators

**Files:**
- Create: `src/Attic.Api/Validators/SendFriendRequestRequestValidator.cs`
- Create: `src/Attic.Api/Validators/OpenPersonalChatRequestValidator.cs`

- [ ] **Step 14.1: `SendFriendRequestRequestValidator.cs`**

```csharp
using Attic.Contracts.Friends;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class SendFriendRequestRequestValidator : AbstractValidator<SendFriendRequestRequest>
{
    public SendFriendRequestRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
        RuleFor(r => r.Text).MaximumLength(500).WithErrorCode("text_too_long");
    }
}
```

- [ ] **Step 14.2: `OpenPersonalChatRequestValidator.cs`**

```csharp
using Attic.Contracts.Friends;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class OpenPersonalChatRequestValidator : AbstractValidator<OpenPersonalChatRequest>
{
    public OpenPersonalChatRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
    }
}
```

- [ ] **Step 14.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Validators \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): add validators for friend-request + open-personal-chat requests"
```

---

## Task 15: `FriendRequestsEndpoints` — create, list, accept, decline (scaffolding)

**Files:**
- Create: `src/Attic.Api/Endpoints/FriendRequestsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Create: `tests/Attic.Api.IntegrationTests/FriendsFlowTests.cs`

- [ ] **Step 15.1: Write `FriendRequestsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
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

public static class FriendRequestsEndpoints
{
    public static IEndpointRouteBuilder MapFriendRequestsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/friend-requests").RequireAuthorization();

        group.MapPost("/", Send);
        group.MapGet("/", ListMine);
        group.MapPost("/{id:guid}/accept", Accept);
        group.MapPost("/{id:guid}/decline", Decline);

        return routes;
    }

    private static async Task<IResult> Send(
        [FromBody] SendFriendRequestRequest req,
        IValidator<SendFriendRequestRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var senderId = currentUser.UserIdOrThrow;
        var recipient = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (recipient is null) return Results.NotFound();
        if (recipient.Id == senderId)
            return Results.BadRequest(new ApiError("self_request", "Cannot send a friend request to yourself."));

        // Canonical order for Friendship lookup.
        var (smaller, larger) = senderId.CompareTo(recipient.Id) < 0 ? (senderId, recipient.Id) : (recipient.Id, senderId);
        var areFriends = await db.Friendships.AsNoTracking()
            .AnyAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);

        var hasPending = await db.FriendRequests.AsNoTracking().AnyAsync(r =>
            ((r.SenderId == senderId && r.RecipientId == recipient.Id) ||
             (r.SenderId == recipient.Id && r.RecipientId == senderId)) &&
            r.Status == FriendRequestStatus.Pending, ct);

        var hasBlock = await db.UserBlocks.AsNoTracking().AnyAsync(b =>
            (b.BlockerId == senderId && b.BlockedId == recipient.Id) ||
            (b.BlockerId == recipient.Id && b.BlockedId == senderId), ct);

        var auth = AuthorizationRules.CanSendFriendRequest(areFriends, hasPending, hasBlock);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot send friend request."));

        var friendReq = FriendRequest.Issue(Guid.NewGuid(), senderId, recipient.Id, req.Text, clock.UtcNow);
        db.FriendRequests.Add(friendReq);
        await db.SaveChangesAsync(ct);

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == senderId, ct);
        return Results.Ok(new FriendRequestDto(
            friendReq.Id, senderId, sender.Username, recipient.Id, recipient.Username,
            friendReq.Text, friendReq.Status.ToString().ToLowerInvariant(),
            friendReq.CreatedAt, friendReq.DecidedAt));
    }

    private static async Task<IResult> ListMine(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        // Both incoming + outgoing pending.
        var rows = await db.FriendRequests.AsNoTracking()
            .Where(r => (r.SenderId == userId || r.RecipientId == userId) &&
                         r.Status == FriendRequestStatus.Pending)
            .Join(db.Users.IgnoreQueryFilters(), r => r.SenderId, u => u.Id, (r, u) => new { Req = r, SenderName = u.Username })
            .Join(db.Users.IgnoreQueryFilters(), x => x.Req.RecipientId, u => u.Id,
                  (x, u) => new FriendRequestDto(
                      x.Req.Id, x.Req.SenderId, x.SenderName,
                      x.Req.RecipientId, u.Username, x.Req.Text,
                      x.Req.Status.ToString().ToLowerInvariant(),
                      x.Req.CreatedAt, x.Req.DecidedAt))
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

        var req = await db.FriendRequests.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientId == userId, ct);
        if (req is null) return Results.NotFound();
        if (req.Status != FriendRequestStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Friend request is no longer pending."));

        req.Accept(clock.UtcNow);
        db.Friendships.Add(Friendship.Create(req.SenderId, req.RecipientId, clock.UtcNow));
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

        var req = await db.FriendRequests.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.RecipientId == userId, ct);
        if (req is null) return Results.NotFound();
        if (req.Status != FriendRequestStatus.Pending)
            return Results.BadRequest(new ApiError("not_pending", "Friend request is no longer pending."));

        req.Decline(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
```

- [ ] **Step 15.2: Register in `Program.cs`**

After `app.MapInvitationsEndpoints();`:

```csharp
app.MapFriendRequestsEndpoints();
```

- [ ] **Step 15.3: Write `tests/Attic.Api.IntegrationTests/FriendsFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class FriendsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Send_and_accept_friend_request_creates_friendship()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, "hi"), ct);
        send.EnsureSuccessStatusCode();
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        dto.Status.ShouldBe("pending");
        dto.Text.ShouldBe("hi");

        var inbox = await recipient.GetAsync("/api/friend-requests", ct);
        var pending = (await inbox.Content.ReadFromJsonAsync<List<FriendRequestDto>>(ct))!;
        pending.ShouldContain(r => r.Id == dto.Id);

        var accept = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct);
        accept.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Friendship shows up in both users' friends list (tested in Task 17).
    }

    [Fact]
    public async Task Decline_marks_declined_and_no_friendship_created()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;

        var decline = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/decline", null, ct);
        decline.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var repeat = await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct);
        repeat.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await repeat.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("not_pending");
    }

    [Fact]
    public async Task Cannot_send_duplicate_pending_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (_, recipientUsername, _) = await Register(ct);

        (await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct)).EnsureSuccessStatusCode();

        var second = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await second.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("DuplicateFriendRequest");
    }

    [Fact]
    public async Task Cannot_send_to_self()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, username, _) = await Register(ct);

        var send = await client.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(username, null), ct);
        send.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await send.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("self_request");
    }
}
```

- [ ] **Step 15.4: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "FriendsFlowTests"
git add src/Attic.Api/Endpoints/FriendRequestsEndpoints.cs \
        src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/FriendsFlowTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): friend-requests endpoints (send, list, accept, decline)"
```

Expected: 4 new tests passing.

---

## Task 16: `FriendsEndpoints` — list + remove

**Files:**
- Create: `src/Attic.Api/Endpoints/FriendsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Modify: `tests/Attic.Api.IntegrationTests/FriendsFlowTests.cs`

- [ ] **Step 16.1: Write `FriendsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class FriendsEndpoints
{
    public static IEndpointRouteBuilder MapFriendsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/friends").RequireAuthorization();

        group.MapGet("/", ListFriends);
        group.MapDelete("/{userId:guid}", RemoveFriend);

        return routes;
    }

    private static async Task<IResult> ListFriends(
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var userId = currentUser.UserIdOrThrow;

        var rows = await db.Friendships.AsNoTracking()
            .Where(f => f.UserAId == userId || f.UserBId == userId)
            .Select(f => new { OtherId = f.UserAId == userId ? f.UserBId : f.UserAId, f.CreatedAt })
            .Join(db.Users.IgnoreQueryFilters(), x => x.OtherId, u => u.Id,
                  (x, u) => new FriendDto(u.Id, u.Username, x.CreatedAt))
            .OrderBy(f => f.Username)
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> RemoveFriend(
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        if (me == userId) return Results.BadRequest(new ApiError("self_target", "Cannot target yourself."));

        var (smaller, larger) = me.CompareTo(userId) < 0 ? (me, userId) : (userId, me);
        var friendship = await db.Friendships.AsTracking()
            .FirstOrDefaultAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        if (friendship is null) return Results.NotFound();

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
```

- [ ] **Step 16.2: Register in `Program.cs`**

```csharp
app.MapFriendsEndpoints();
```

- [ ] **Step 16.3: Append test to `FriendsFlowTests.cs`**

```csharp
    [Fact]
    public async Task List_friends_returns_both_sides_after_accept()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, senderUsername, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        var senderFriends = await sender.GetAsync("/api/friends", ct);
        var senderList = (await senderFriends.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        senderList.ShouldContain(f => f.Username == recipientUsername);

        var recipientFriends = await recipient.GetAsync("/api/friends", ct);
        var recipientList = (await recipientFriends.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        recipientList.ShouldContain(f => f.Username == senderUsername);
    }

    [Fact]
    public async Task Remove_friend_clears_friendship_both_directions()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sender, _, _) = await Register(ct);
        var (recipient, recipientUsername, _) = await Register(ct);

        var send = await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await recipient.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        // Resolve recipient's userId via their friends list (saves a roundtrip).
        var senderFriends = await sender.GetAsync("/api/friends", ct);
        var senderList = (await senderFriends.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        var recipientId = senderList.First(f => f.Username == recipientUsername).UserId;

        var del = await sender.DeleteAsync($"/api/friends/{recipientId:D}", ct);
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await recipient.GetAsync("/api/friends", ct);
        var afterList = (await after.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        afterList.ShouldNotContain(f => f.UserId == recipientId);
    }
```

- [ ] **Step 16.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "FriendsFlowTests"
git add src/Attic.Api/Endpoints/FriendsEndpoints.cs \
        src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/FriendsFlowTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): GET /api/friends + DELETE /api/friends/{userId}"
```

---

## Task 17: `UsersEndpoints` — search + block + unblock

**Files:**
- Create: `src/Attic.Api/Endpoints/UsersEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Create: `tests/Attic.Api.IntegrationTests/BlocksFlowTests.cs`

Blocking has the heaviest side effects of Phase 3:

1. Insert `UserBlock(BlockerId = me, BlockedId = target)`.
2. Delete any `Friendship` between the two users.
3. Mark all pending `FriendRequest` rows between them as `Cancelled`.
4. The existing personal `Channel` (if any) stays — the `CanPostInPersonalChat` rule hides posting.

- [ ] **Step 17.1: Write `UsersEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/search", Search);
        group.MapPost("/{userId:guid}/block", Block);
        group.MapDelete("/{userId:guid}/block", Unblock);

        return routes;
    }

    private static async Task<IResult> Search(
        string q,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Results.Ok(Array.Empty<UserSearchResult>());

        var me = currentUser.UserIdOrThrow;
        var prefix = q.Trim().ToLower();

        var rows = await db.Users.AsNoTracking()
            .Where(u => u.Id != me && EF.Functions.Like(u.Username.ToLower(), prefix + "%"))
            .OrderBy(u => u.Username)
            .Take(20)
            .Select(u => new UserSearchResult(u.Id, u.Username))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> Block(
        Guid userId,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;
        if (me == userId) return Results.BadRequest(new ApiError("self_target", "Cannot block yourself."));

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (target is null) return Results.NotFound();

        var existingBlock = await db.UserBlocks.AsNoTracking()
            .AnyAsync(b => b.BlockerId == me && b.BlockedId == userId, ct);
        if (existingBlock) return Results.NoContent();   // Idempotent.

        // Insert block.
        db.UserBlocks.Add(UserBlock.Create(me, userId, clock.UtcNow));

        // Remove friendship (canonical order).
        var (smaller, larger) = me.CompareTo(userId) < 0 ? (me, userId) : (userId, me);
        var friendship = await db.Friendships.AsTracking()
            .FirstOrDefaultAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        if (friendship is not null) db.Friendships.Remove(friendship);

        // Cancel pending requests both ways.
        var pending = await db.FriendRequests.AsTracking()
            .Where(r => ((r.SenderId == me && r.RecipientId == userId) ||
                         (r.SenderId == userId && r.RecipientId == me)) &&
                         r.Status == FriendRequestStatus.Pending)
            .ToListAsync(ct);
        foreach (var p in pending) p.Cancel(clock.UtcNow);

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Unblock(
        Guid userId,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var me = currentUser.UserIdOrThrow;

        var block = await db.UserBlocks.AsTracking()
            .FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == userId, ct);
        if (block is null) return Results.NotFound();

        db.UserBlocks.Remove(block);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
```

- [ ] **Step 17.2: Register in `Program.cs`**

```csharp
app.MapUsersEndpoints();
```

- [ ] **Step 17.3: Write `tests/Attic.Api.IntegrationTests/BlocksFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class BlocksFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    [Fact]
    public async Task Search_matches_username_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        var (me, _, _) = await Register(ct);
        var (_, targetUsername, _) = await Register(ct);

        var resp = await me.GetAsync($"/api/users/search?q={targetUsername[..4]}", ct);
        resp.EnsureSuccessStatusCode();
        var rows = (await resp.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!;
        rows.ShouldContain(r => r.Username == targetUsername);
    }

    [Fact]
    public async Task Block_removes_friendship_and_cancels_pending_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);

        // Alice sends friend request → Bob accepts → friendship established.
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await bob.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();

        // Resolve Bob's userId via Alice's friends list.
        var aliceFriends = await alice.GetAsync("/api/friends", ct);
        var bobId = (await aliceFriends.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!
            .First(f => f.Username == bobUsername).UserId;

        // Alice blocks Bob.
        var block = await alice.PostAsync($"/api/users/{bobId:D}/block", null, ct);
        block.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Friendship gone.
        var after = await alice.GetAsync("/api/friends", ct);
        var afterList = (await after.Content.ReadFromJsonAsync<List<FriendDto>>(ct))!;
        afterList.ShouldNotContain(f => f.UserId == bobId);

        // New friend request from Alice to Bob is denied.
        var retry = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        retry.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unblock_reenables_friend_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);

        // Look up Bob's id via search.
        var search = await alice.GetAsync($"/api/users/search?q={bobUsername[..4]}", ct);
        var bobId = (await search.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!
            .First(u => u.Username == bobUsername).Id;

        // Alice blocks then unblocks.
        (await alice.PostAsync($"/api/users/{bobId:D}/block", null, ct)).EnsureSuccessStatusCode();
        (await alice.DeleteAsync($"/api/users/{bobId:D}/block", ct)).EnsureSuccessStatusCode();

        // Alice can now send a friend request.
        var send = await alice.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bobUsername, null), ct);
        send.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 17.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "BlocksFlowTests|FriendsFlowTests"
git add src/Attic.Api/Endpoints/UsersEndpoints.cs \
        src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/BlocksFlowTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): user search + block + unblock"
```

Expected: 3 new BlocksFlowTests passing + all FriendsFlowTests still passing.

---

## Task 18: `PersonalChatsEndpoints` — open-or-create

**Files:**
- Create: `src/Attic.Api/Endpoints/PersonalChatsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`
- Create: `tests/Attic.Api.IntegrationTests/PersonalChatFlowTests.cs`

`POST /api/personal-chats/open { username }` — idempotent. If a personal `Channel` already exists between the caller and the target (two members, `Kind = Personal`), returns that channel's `ChannelDetails`. Otherwise creates one, adding both users as `ChannelMember` with `Role = Member`, `OwnerId = null`, `Name = null`.

Precondition: caller and target are friends AND no block in either direction. Enforced by `AuthorizationRules.CanOpenPersonalChat`.

- [ ] **Step 18.1: Write `PersonalChatsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
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

public static class PersonalChatsEndpoints
{
    public static IEndpointRouteBuilder MapPersonalChatsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/personal-chats").RequireAuthorization();
        group.MapPost("/open", Open);
        return routes;
    }

    private static async Task<IResult> Open(
        [FromBody] OpenPersonalChatRequest req,
        IValidator<OpenPersonalChatRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid)
            return Results.BadRequest(new ApiError(vr.Errors[0].ErrorCode, vr.Errors[0].ErrorMessage));

        var me = currentUser.UserIdOrThrow;
        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == req.Username, ct);
        if (target is null) return Results.NotFound();
        if (target.Id == me) return Results.BadRequest(new ApiError("self_target", "Cannot open a personal chat with yourself."));

        var (smaller, larger) = me.CompareTo(target.Id) < 0 ? (me, target.Id) : (target.Id, me);
        var areFriends = await db.Friendships.AsNoTracking()
            .AnyAsync(f => f.UserAId == smaller && f.UserBId == larger, ct);
        var hasBlock = await db.UserBlocks.AsNoTracking().AnyAsync(b =>
            (b.BlockerId == me && b.BlockedId == target.Id) ||
            (b.BlockerId == target.Id && b.BlockedId == me), ct);

        var auth = AuthorizationRules.CanOpenPersonalChat(areFriends, hasBlock);
        if (!auth.Allowed) return Results.BadRequest(new ApiError(auth.Reason.ToString(), "Cannot open personal chat."));

        // Try to find an existing personal channel whose two members are exactly {me, target}.
        var existing = await (
            from c in db.Channels.AsNoTracking()
            where c.Kind == ChannelKind.Personal
            let memberIds = db.ChannelMembers.Where(m => m.ChannelId == c.Id).Select(m => m.UserId).ToList()
            where memberIds.Count == 2 && memberIds.Contains(me) && memberIds.Contains(target.Id)
            select c).FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return Results.Ok(new ChannelDetails(
                existing.Id, "personal", existing.Name, existing.Description,
                existing.OwnerId, existing.CreatedAt, MemberCount: 2));
        }

        var channel = Channel.CreatePersonal(Guid.NewGuid(), clock.UtcNow);
        db.Channels.Add(channel);
        db.ChannelMembers.Add(ChannelMember.Join(channel.Id, me, ChannelRole.Member, clock.UtcNow));
        db.ChannelMembers.Add(ChannelMember.Join(channel.Id, target.Id, ChannelRole.Member, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        return Results.Ok(new ChannelDetails(
            channel.Id, "personal", null, null, null, channel.CreatedAt, MemberCount: 2));
    }
}
```

- [ ] **Step 18.2: Register in `Program.cs`**

```csharp
app.MapPersonalChatsEndpoints();
```

- [ ] **Step 18.3: Write `tests/Attic.Api.IntegrationTests/PersonalChatFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Friends;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class PersonalChatFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    private static async Task MakeFriends(HttpClient a, HttpClient b, string bUsername, CancellationToken ct)
    {
        var send = await a.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(bUsername, null), ct);
        var dto = (await send.Content.ReadFromJsonAsync<FriendRequestDto>(ct))!;
        (await b.PostAsync($"/api/friend-requests/{dto.Id:D}/accept", null, ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Open_personal_chat_creates_channel_with_two_members()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);
        await MakeFriends(alice, bob, bobUsername, ct);

        var open = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        open.StatusCode.ShouldBe(HttpStatusCode.OK);
        var channel = (await open.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
        channel.Kind.ShouldBe("personal");
        channel.Name.ShouldBeNull();
        channel.OwnerId.ShouldBeNull();
        channel.MemberCount.ShouldBe(2);

        // Both users see it in their /api/channels/mine list.
        var aliceMine = (await (await alice.GetAsync("/api/channels/mine", ct))
            .Content.ReadFromJsonAsync<List<ChannelSummary>>(ct))!;
        aliceMine.ShouldContain(c => c.Id == channel.Id && c.Kind == "personal");

        var bobMine = (await (await bob.GetAsync("/api/channels/mine", ct))
            .Content.ReadFromJsonAsync<List<ChannelSummary>>(ct))!;
        bobMine.ShouldContain(c => c.Id == channel.Id && c.Kind == "personal");
    }

    [Fact]
    public async Task Open_personal_chat_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (bob, bobUsername, _) = await Register(ct);
        await MakeFriends(alice, bob, bobUsername, ct);

        var first = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        var c1 = (await first.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var second = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        var c2 = (await second.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        c1.Id.ShouldBe(c2.Id);
    }

    [Fact]
    public async Task Open_rejected_when_not_friends()
    {
        var ct = TestContext.Current.CancellationToken;
        var (alice, _, _) = await Register(ct);
        var (_, bobUsername, _) = await Register(ct);

        var open = await alice.PostAsJsonAsync("/api/personal-chats/open",
            new OpenPersonalChatRequest(bobUsername), ct);
        open.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var err = (await open.Content.ReadFromJsonAsync<ApiError>(ct))!;
        err.Code.ShouldBe("NotFriends");
    }
}
```

- [ ] **Step 18.4: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "PersonalChatFlowTests"
git add src/Attic.Api/Endpoints/PersonalChatsEndpoints.cs \
        src/Attic.Api/Program.cs \
        tests/Attic.Api.IntegrationTests/PersonalChatFlowTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): POST /api/personal-chats/open (idempotent)"
```

Expected: 3 new tests passing.

---

## Task 19: Checkpoint 2 sweep

**Files:** none

- [ ] **Step 19.1: Full test run**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: Domain ~100 + Integration 33 (Phase-2) + 4 Friends + 3 Blocks + 3 PersonalChat = 43 passing.

- [ ] **Step 19.2: Commit marker**

```bash
git commit --allow-empty -m "chore: Phase 3 Checkpoint 2 (REST + integration tests) green"
```

---

## Task 20: `FriendsEventBroadcaster` — IHubContext helper

**Files:**
- Create: `src/Attic.Api/Hubs/FriendsEventBroadcaster.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 20.1: Write `FriendsEventBroadcaster.cs`**

```csharp
using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class FriendsEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task FriendRequestReceived(Guid recipientId, FriendRequestDto dto) =>
        hub.Clients.Group(GroupNames.User(recipientId)).SendAsync("FriendRequestReceived", dto);

    public Task FriendRequestDecided(Guid senderId, Guid requestId, string status) =>
        hub.Clients.Group(GroupNames.User(senderId)).SendAsync("FriendRequestDecided", requestId, status);

    public Task FriendRemoved(Guid notifyUserId, Guid otherUserId) =>
        hub.Clients.Group(GroupNames.User(notifyUserId)).SendAsync("FriendRemoved", otherUserId);

    public Task Blocked(Guid blockedUserId, Guid blockerId) =>
        hub.Clients.Group(GroupNames.User(blockedUserId)).SendAsync("Blocked", blockerId);
}
```

- [ ] **Step 20.2: Register in `Program.cs`** (near the existing `ChannelEventBroadcaster` registration):

```csharp
builder.Services.AddScoped<Attic.Api.Hubs.FriendsEventBroadcaster>();
```

- [ ] **Step 20.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/FriendsEventBroadcaster.cs \
        src/Attic.Api/Program.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): add FriendsEventBroadcaster IHubContext helper"
```

---

## Task 21: Wire broadcasts into friend + block endpoints

**Files:**
- Modify: `src/Attic.Api/Endpoints/FriendRequestsEndpoints.cs`
- Modify: `src/Attic.Api/Endpoints/FriendsEndpoints.cs`
- Modify: `src/Attic.Api/Endpoints/UsersEndpoints.cs`

Inject `FriendsEventBroadcaster events` into each handler below and publish after `SaveChangesAsync`:

- [ ] **Step 21.1: `Send` (FriendRequestsEndpoints)**

After `await db.SaveChangesAsync(ct);`, before the `Results.Ok(...)`:

```csharp
        var dto = new FriendRequestDto(
            friendReq.Id, senderId, sender.Username, recipient.Id, recipient.Username,
            friendReq.Text, friendReq.Status.ToString().ToLowerInvariant(),
            friendReq.CreatedAt, friendReq.DecidedAt);
        await events.FriendRequestReceived(recipient.Id, dto);
        return Results.Ok(dto);
```

(Replace the existing trailing `Results.Ok(...)`.)

- [ ] **Step 21.2: `Accept` (FriendRequestsEndpoints)**

After `await db.SaveChangesAsync(ct);`:

```csharp
        await events.FriendRequestDecided(req.SenderId, req.Id, "accepted");
```

Also broadcast a symmetric `FriendRequestDecided` to the recipient too? No — spec only sends to the sender who was waiting.

- [ ] **Step 21.3: `Decline` (FriendRequestsEndpoints)**

```csharp
        await events.FriendRequestDecided(req.SenderId, req.Id, "declined");
```

- [ ] **Step 21.4: `RemoveFriend` (FriendsEndpoints)**

After `await db.SaveChangesAsync(ct);`, broadcast `FriendRemoved` to both participants:

```csharp
        await events.FriendRemoved(me, userId);
        await events.FriendRemoved(userId, me);
```

- [ ] **Step 21.5: `Block` (UsersEndpoints)**

After `await db.SaveChangesAsync(ct);`, notify the target + the actor so both sides update:

```csharp
        await events.Blocked(userId, me);
        // Also notify the caller's own tabs so their UI updates.
        await events.FriendRemoved(me, userId);
```

- [ ] **Step 21.6: Add `FriendsEventBroadcaster events` to each handler signature**

Example for `Send`:

```csharp
    private static async Task<IResult> Send(
        [FromBody] SendFriendRequestRequest req,
        IValidator<SendFriendRequestRequest> validator,
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        Attic.Api.Hubs.FriendsEventBroadcaster events,
        CancellationToken ct)
```

Apply the same injection addition to `Accept`, `Decline`, `RemoveFriend`, `Block`.

- [ ] **Step 21.7: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/FriendRequestsEndpoints.cs \
        src/Attic.Api/Endpoints/FriendsEndpoints.cs \
        src/Attic.Api/Endpoints/UsersEndpoints.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(api): emit SignalR friend + block events from REST endpoints"
```

Expected: 0/0.

---

## Task 22: `ChatHub.SendMessage` — enforce personal-chat gate

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

Phase 2's `SendMessage` already authorizes via `CanPostInChannel(member)`. For personal channels, we also need to check `CanPostInPersonalChat(areFriends, hasBlock)`.

- [ ] **Step 22.1: Insert the personal-chat gate after the member lookup + existing auth**

Find the block in `SendMessage` that reads:

```csharp
var auth = AuthorizationRules.CanPostInChannel(member);
if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());
```

Immediately after (before the message-insert block), insert:

```csharp
        // Personal-chat: friendship + no-block gate.
        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId);
        if (channel is { Kind: Attic.Domain.Enums.ChannelKind.Personal })
        {
            // The two members are me + the other participant — look up the other id.
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
```

- [ ] **Step 22.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "fix(api): gate SendMessage in personal chats on friendship + no-block"
```

---

## Task 23: Realtime integration tests — FriendRequestReceived + Blocked

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/RealtimeFriendEventsTests.cs`

- [ ] **Step 23.1: Write the file**

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Friends;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class RealtimeFriendEventsTests(AppHostFixture fx)
{
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
    public async Task FriendRequestReceived_arrives_over_hub()
    {
        var ct = TestContext.Current.CancellationToken;

        var senderHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var sender = new HttpClient(senderHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await sender.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"a-{Guid.NewGuid():N}@example.com", $"a{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var recipientHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var recipient = new HttpClient(recipientHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var recipientUsername = $"r{Random.Shared.Next():x}";
        (await recipient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"r-{Guid.NewGuid():N}@example.com", recipientUsername, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        await using var recipientHub = await ConnectHub(fx, recipientHandler, ct);
        var received = new TaskCompletionSource<FriendRequestDto>();
        recipientHub.On<FriendRequestDto>("FriendRequestReceived", dto => received.TrySetResult(dto));

        (await sender.PostAsJsonAsync("/api/friend-requests",
            new SendFriendRequestRequest(recipientUsername, "ping"), ct)).EnsureSuccessStatusCode();

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.RecipientUsername.ShouldBe(recipientUsername);
        evt.Text.ShouldBe("ping");
    }

    [Fact]
    public async Task Blocked_event_fires_on_recipient()
    {
        var ct = TestContext.Current.CancellationToken;

        var blockerHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var blocker = new HttpClient(blockerHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        (await blocker.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"bl-{Guid.NewGuid():N}@example.com", $"bl{Random.Shared.Next():x}", "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var blockedHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var blocked = new HttpClient(blockedHandler) { BaseAddress = fx.ApiClient.BaseAddress };
        var blockedUsername = $"blocked{Random.Shared.Next():x}";
        (await blocked.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"b-{Guid.NewGuid():N}@example.com", blockedUsername, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        // Resolve blocked userId via search.
        var search = await blocker.GetAsync($"/api/users/search?q={blockedUsername[..4]}", ct);
        var blockedId = (await search.Content.ReadFromJsonAsync<List<UserSearchResult>>(ct))!
            .First(u => u.Username == blockedUsername).Id;

        await using var blockedHub = await ConnectHub(fx, blockedHandler, ct);
        var blockEvent = new TaskCompletionSource<Guid>();
        blockedHub.On<Guid>("Blocked", blockerId => blockEvent.TrySetResult(blockerId));

        (await blocker.PostAsync($"/api/users/{blockedId:D}/block", null, ct)).EnsureSuccessStatusCode();

        var evt = await blockEvent.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        evt.ShouldNotBe(Guid.Empty);
    }
}
```

- [ ] **Step 23.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "RealtimeFriendEventsTests"
git add tests/Attic.Api.IntegrationTests/RealtimeFriendEventsTests.cs \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "test(api): realtime friend + block event delivery"
```

Expected: 2 new tests passing.

---

## Task 24: Checkpoint 3 sweep

**Files:** none

- [ ] **Step 24.1: Full run**

```bash
dotnet test
```

Expected: Domain ~100, Integration ~45 (Phase-2's 33 + 4 Friends + 3 Blocks + 3 PersonalChat + 2 RealtimeFriend) = all green.

- [ ] **Step 24.2: Commit marker**

```bash
git commit --allow-empty -m "chore: Phase 3 Checkpoint 3 (hub realtime) green"
```

---

## Task 25: Frontend — types + API clients

**Files:**
- Modify: `src/Attic.Web/src/types.ts`
- Create: `src/Attic.Web/src/api/friends.ts`
- Create: `src/Attic.Web/src/api/users.ts`
- Create: `src/Attic.Web/src/api/personalChats.ts`

- [ ] **Step 25.1: Append to `types.ts`**

```ts
export interface FriendRequestDto {
  id: string;
  senderId: string;
  senderUsername: string;
  recipientId: string;
  recipientUsername: string;
  text: string | null;
  status: string;
  createdAt: string;
  decidedAt: string | null;
}

export interface FriendDto {
  userId: string;
  username: string;
  friendsSince: string;
}

export interface SendFriendRequestRequest {
  username: string;
  text: string | null;
}

export interface UserSearchResult {
  id: string;
  username: string;
}

export interface OpenPersonalChatRequest {
  username: string;
}
```

- [ ] **Step 25.2: `src/Attic.Web/src/api/friends.ts`**

```ts
import { api } from './client';
import type { FriendDto, FriendRequestDto, SendFriendRequestRequest } from '../types';

export const friendsApi = {
  listFriends: () => api.get<FriendDto[]>('/api/friends'),
  removeFriend: (userId: string) => api.delete<void>(`/api/friends/${userId}`),
  listRequests: () => api.get<FriendRequestDto[]>('/api/friend-requests'),
  send: (req: SendFriendRequestRequest) => api.post<FriendRequestDto>('/api/friend-requests', req),
  accept: (id: string) => api.post<void>(`/api/friend-requests/${id}/accept`),
  decline: (id: string) => api.post<void>(`/api/friend-requests/${id}/decline`),
};
```

- [ ] **Step 25.3: `src/Attic.Web/src/api/users.ts`**

```ts
import { api } from './client';
import type { UserSearchResult } from '../types';

export const usersApi = {
  search: (q: string) => api.get<UserSearchResult[]>(`/api/users/search?q=${encodeURIComponent(q)}`),
  block: (userId: string) => api.post<void>(`/api/users/${userId}/block`),
  unblock: (userId: string) => api.delete<void>(`/api/users/${userId}/block`),
};
```

- [ ] **Step 25.4: `src/Attic.Web/src/api/personalChats.ts`**

```ts
import { api } from './client';
import type { ChannelDetails } from '../types';
import type { OpenPersonalChatRequest } from '../types';

export const personalChatsApi = {
  open: (req: OpenPersonalChatRequest) => api.post<ChannelDetails>('/api/personal-chats/open', req),
};
```

- [ ] **Step 25.5: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/types.ts \
        src/Attic.Web/src/api/friends.ts \
        src/Attic.Web/src/api/users.ts \
        src/Attic.Web/src/api/personalChats.ts \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): typed Phase 3 DTOs + friends/users/personal-chats API clients"
```

---

## Task 26: Frontend — extend SignalR wrapper with friend + block events

**Files:**
- Modify: `src/Attic.Web/src/api/signalr.ts`

Append four new event hooks to the `HubClient` interface and the factory's returned singleton:

- [ ] **Step 26.1: In the `HubClient` interface, append**

```ts
  onFriendRequestReceived(cb: (dto: FriendRequestDto) => void): () => void;
  onFriendRequestDecided(cb: (requestId: string, status: string) => void): () => void;
  onFriendRemoved(cb: (otherUserId: string) => void): () => void;
  onBlocked(cb: (blockerId: string) => void): () => void;
```

- [ ] **Step 26.2: Add `FriendRequestDto` to the import**

```ts
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto, FriendRequestDto } from '../types';
```

- [ ] **Step 26.3: In the singleton factory, add the four `on<...>` entries at the bottom of the returned object (after `onInvitationReceived`)**

```ts
    onFriendRequestReceived: (cb) => on<[FriendRequestDto]>('FriendRequestReceived', cb),
    onFriendRequestDecided: (cb) => on<[string, string]>('FriendRequestDecided', cb),
    onFriendRemoved: (cb) => on<[string]>('FriendRemoved', cb),
    onBlocked: (cb) => on<[string]>('Blocked', cb),
```

- [ ] **Step 26.4: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/api/signalr.ts \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): SignalR client handles friend + block events"
```

---

## Task 27: Frontend — `useFriends`, `useFriendRequests`, `useUserSearch` hooks

**Files:**
- Create: `src/Attic.Web/src/chat/useFriends.ts`
- Create: `src/Attic.Web/src/chat/useFriendRequests.ts`
- Create: `src/Attic.Web/src/chat/useUserSearch.ts`

- [ ] **Step 27.1: `useFriends.ts`**

```ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { friendsApi } from '../api/friends';
import { getOrCreateHubClient } from '../api/signalr';

export function useFriends() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['friends'] as const,
    queryFn: () => friendsApi.listFriends(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['friends'] }); };
    const off1 = hub.onFriendRequestDecided(invalidate);
    const off2 = hub.onFriendRemoved(invalidate);
    const off3 = hub.onBlocked(invalidate);
    return () => { off1(); off2(); off3(); };
  }, [qc]);

  return q;
}
```

- [ ] **Step 27.2: `useFriendRequests.ts`**

```ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { friendsApi } from '../api/friends';
import { getOrCreateHubClient } from '../api/signalr';

export function useFriendRequests() {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['friend-requests'] as const,
    queryFn: () => friendsApi.listRequests(),
    staleTime: 30_000,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    const invalidate = () => { void qc.invalidateQueries({ queryKey: ['friend-requests'] }); };
    const off1 = hub.onFriendRequestReceived(invalidate);
    const off2 = hub.onFriendRequestDecided(invalidate);
    return () => { off1(); off2(); };
  }, [qc]);

  return q;
}
```

- [ ] **Step 27.3: `useUserSearch.ts`**

```ts
import { useQuery } from '@tanstack/react-query';
import { usersApi } from '../api/users';

export function useUserSearch(query: string) {
  return useQuery({
    queryKey: ['user-search', query] as const,
    queryFn: () => usersApi.search(query),
    enabled: query.length >= 2,
    staleTime: 10_000,
  });
}
```

- [ ] **Step 27.4: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/useFriends.ts \
        src/Attic.Web/src/chat/useFriendRequests.ts \
        src/Attic.Web/src/chat/useUserSearch.ts \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): friend + user-search hooks with realtime invalidation"
```

---

## Task 28: Frontend — `SendFriendRequestModal`

**Files:**
- Create: `src/Attic.Web/src/chat/SendFriendRequestModal.tsx`

- [ ] **Step 28.1: Write the file**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { friendsApi } from '../api/friends';
import type { ApiError } from '../types';
import { useUserSearch } from './useUserSearch';

export function SendFriendRequestModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const { data: matches } = useUserSearch(query);

  const send = useMutation({
    mutationFn: () => friendsApi.send({ username: selected!, text: text.trim() || null }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friend-requests'] });
      onClose();
    },
    onError: (err: ApiError) => setError(err?.message ?? err?.code ?? 'Send failed'),
  });

  return (
    <div className="fixed inset-0 bg-black/30 flex items-center justify-center" onClick={onClose}>
      <div className="bg-white rounded-xl shadow p-6 w-96 space-y-3" onClick={e => e.stopPropagation()}>
        <h2 className="font-semibold">Send friend request</h2>
        <input className="w-full border rounded px-3 py-2"
               placeholder="Search by username…"
               value={query}
               onChange={e => { setQuery(e.target.value); setSelected(null); }} />
        {query.length >= 2 && matches && matches.length > 0 && !selected && (
          <ul className="border rounded divide-y max-h-40 overflow-y-auto">
            {matches.map(u => (
              <li key={u.id}>
                <button onClick={() => { setSelected(u.username); setQuery(u.username); }}
                        className="w-full text-left px-3 py-1 hover:bg-slate-50 text-sm">
                  {u.username}
                </button>
              </li>
            ))}
          </ul>
        )}
        <textarea className="w-full border rounded px-3 py-2 text-sm" rows={3}
                  placeholder="Optional message (max 500 chars)"
                  value={text} maxLength={500}
                  onChange={e => setText(e.target.value)} />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="px-3 py-1 text-sm">Cancel</button>
          <button onClick={() => send.mutate()} disabled={!selected || send.isPending}
                  className="px-3 py-1 text-sm bg-blue-600 text-white rounded disabled:opacity-50">
            {send.isPending ? 'Sending…' : 'Send'}
          </button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 28.2: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/SendFriendRequestModal.tsx \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): SendFriendRequestModal with user search"
```

---

## Task 29: Frontend — `Contacts` page

**Files:**
- Create: `src/Attic.Web/src/chat/Contacts.tsx`
- Create: `src/Attic.Web/src/chat/useOpenPersonalChat.ts`

Shows three sections: Incoming requests, Outgoing requests (pending only, for visibility), Friends. Offers per-friend: "Open chat" (calls `personalChatsApi.open` then navigates), "Remove", "Block". Outgoing requests have a "Cancel" action.

- [ ] **Step 29.1: `useOpenPersonalChat.ts`**

```ts
import { useCallback } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { personalChatsApi } from '../api/personalChats';

export function useOpenPersonalChat() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const mutation = useMutation({
    mutationFn: (username: string) => personalChatsApi.open({ username }),
    onSuccess: (channel) => {
      void qc.invalidateQueries({ queryKey: ['channels', 'mine'] });
      navigate(`/chat/${channel.id}`);
    },
  });
  return useCallback((username: string) => mutation.mutate(username), [mutation]);
}
```

- [ ] **Step 29.2: `Contacts.tsx`**

```tsx
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { friendsApi } from '../api/friends';
import { usersApi } from '../api/users';
import { useAuth } from '../auth/useAuth';
import { useFriends } from './useFriends';
import { useFriendRequests } from './useFriendRequests';
import { useOpenPersonalChat } from './useOpenPersonalChat';
import { SendFriendRequestModal } from './SendFriendRequestModal';

export function Contacts() {
  const { user } = useAuth();
  const qc = useQueryClient();
  const openChat = useOpenPersonalChat();
  const [modalOpen, setModalOpen] = useState(false);

  const { data: friends } = useFriends();
  const { data: requests } = useFriendRequests();
  const incoming = (requests ?? []).filter(r => r.recipientId === user?.id);
  const outgoing = (requests ?? []).filter(r => r.senderId === user?.id);

  const accept = useMutation({
    mutationFn: (id: string) => friendsApi.accept(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['friend-requests'] });
      void qc.invalidateQueries({ queryKey: ['friends'] });
    },
  });
  const decline = useMutation({
    mutationFn: (id: string) => friendsApi.decline(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friend-requests'] }); },
  });
  const remove = useMutation({
    mutationFn: (userId: string) => friendsApi.removeFriend(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });
  const block = useMutation({
    mutationFn: (userId: string) => usersApi.block(userId),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['friends'] }); },
  });

  return (
    <div className="flex-1 flex flex-col p-6 overflow-y-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Contacts</h1>
        <button onClick={() => setModalOpen(true)}
                className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
          + Send friend request
        </button>
      </div>

      {incoming.length > 0 && (
        <section className="mb-6">
          <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Incoming</h2>
          <ul className="divide-y bg-white rounded border">
            {incoming.map(r => (
              <li key={r.id} className="flex items-center justify-between px-4 py-2">
                <div>
                  <div className="font-medium">{r.senderUsername}</div>
                  {r.text && <div className="text-sm text-slate-500">{r.text}</div>}
                </div>
                <div className="flex gap-2">
                  <button onClick={() => accept.mutate(r.id)}
                          className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                    Accept
                  </button>
                  <button onClick={() => decline.mutate(r.id)} className="px-3 py-1 text-sm">
                    Decline
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {outgoing.length > 0 && (
        <section className="mb-6">
          <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Outgoing</h2>
          <ul className="divide-y bg-white rounded border">
            {outgoing.map(r => (
              <li key={r.id} className="px-4 py-2">
                <div className="font-medium">{r.recipientUsername}</div>
                <div className="text-xs text-slate-400">Pending since {new Date(r.createdAt).toLocaleString()}</div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section>
        <h2 className="text-sm font-semibold text-slate-500 uppercase mb-2">Friends</h2>
        {(friends ?? []).length === 0 && (
          <div className="text-slate-400 bg-white border rounded p-6 text-center">
            No friends yet — send a request to get started.
          </div>
        )}
        <ul className="divide-y bg-white rounded border">
          {(friends ?? []).map(f => (
            <li key={f.userId} className="flex items-center justify-between px-4 py-2">
              <div>
                <div className="font-medium">{f.username}</div>
                <div className="text-xs text-slate-500">Friends since {new Date(f.friendsSince).toLocaleDateString()}</div>
              </div>
              <div className="flex gap-2">
                <button onClick={() => openChat(f.username)}
                        className="px-3 py-1 text-sm bg-blue-600 text-white rounded">
                  Chat
                </button>
                <button onClick={() => remove.mutate(f.userId)} className="px-3 py-1 text-sm">
                  Remove
                </button>
                <button onClick={() => block.mutate(f.userId)} className="px-3 py-1 text-sm text-red-600">
                  Block
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      {modalOpen && <SendFriendRequestModal onClose={() => setModalOpen(false)} />}
    </div>
  );
}
```

- [ ] **Step 29.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/Contacts.tsx \
        src/Attic.Web/src/chat/useOpenPersonalChat.ts \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): Contacts page with friends, incoming/outgoing requests, per-friend actions"
```

---

## Task 30: Frontend — routing to `/contacts` + sidebar link

**Files:**
- Modify: `src/Attic.Web/src/App.tsx`
- Modify: `src/Attic.Web/src/chat/ChatShell.tsx`
- Modify: `src/Attic.Web/src/chat/Sidebar.tsx`

- [ ] **Step 30.1: Add the route in `App.tsx`**

Inside the authenticated `Route element={<AuthGate />}` block, next to the existing `/catalog` and `/invitations` routes, add:

```tsx
          <Route path="/contacts" element={<ChatShell />} />
```

- [ ] **Step 30.2: Render `<Contacts />` in `ChatShell.tsx` when pathname is `/contacts`**

Import:
```tsx
import { Contacts } from './Contacts';
```

Inside `<main>` replace the chain of `pathname` checks with:

```tsx
          {pathname === '/catalog' && <PublicCatalog />}
          {pathname === '/invitations' && <InvitationsInbox />}
          {pathname === '/contacts' && <Contacts />}
          {pathname !== '/catalog' && pathname !== '/invitations' && pathname !== '/contacts' && (
            <>
              <div className="flex-1 flex flex-col"><ChatWindow /></div>
              {channelId && <RoomDetails channelId={channelId} />}
            </>
          )}
```

- [ ] **Step 30.3: Add Contacts link to `Sidebar.tsx`**

Inside the existing `.p-2.border-t` footer block (the one that has the `Invitations` link), change it to:

```tsx
      <div className="p-2 border-t flex gap-2">
        <Link to="/contacts" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Contacts
        </Link>
        <Link to="/invitations" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Invitations
        </Link>
      </div>
```

- [ ] **Step 30.4: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/App.tsx \
        src/Attic.Web/src/chat/ChatShell.tsx \
        src/Attic.Web/src/chat/Sidebar.tsx \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): /contacts route + sidebar link"
```

Expected: 0 lint errors, build clean.

---

## Task 31: Frontend — Personal tab populates from `/api/channels/mine`

**Files:**
- Modify: `src/Attic.Web/src/chat/Sidebar.tsx`

The Phase-2 `Sidebar` already filters by `c.kind === tab`, so personal channels will just show up in the Personal tab once `/api/channels/mine` returns them. Personal channels have `name = null`; the existing label fallback `'Personal chat'` renders them.

This task adds a "+ New personal chat" button to the Personal tab that opens a small username-picker. The flow reuses `useOpenPersonalChat`.

- [ ] **Step 31.1: Add the action button inside `Sidebar.tsx` Personal tab**

Near the top of `Sidebar.tsx`, below the existing imports, import:

```tsx
import { useOpenPersonalChat } from './useOpenPersonalChat';
```

Inside the component body, **above** the existing `channels` constant, add:

```tsx
  const openChat = useOpenPersonalChat();
  function promptAndOpen() {
    const username = window.prompt('Open personal chat with (username):');
    if (username && username.trim().length >= 3) openChat(username.trim());
  }
```

Wrap the `onCreate` button in the header action row inside a conditional — when `tab === 'personal'`, render the `+ New personal chat` button instead of `+ New room`:

Replace the existing `.p-2.border-b.flex.gap-2` block:

```tsx
      <div className="p-2 border-b flex gap-2">
        <Link to="/catalog" className="flex-1 text-center text-xs px-2 py-1 border rounded hover:bg-slate-50">
          Catalog
        </Link>
        {tab === 'personal' ? (
          <button onClick={promptAndOpen} className="flex-1 text-xs px-2 py-1 border rounded hover:bg-slate-50">
            + New personal chat
          </button>
        ) : (
          <button onClick={onCreate} className="flex-1 text-xs px-2 py-1 border rounded hover:bg-slate-50">
            + New room
          </button>
        )}
      </div>
```

`window.prompt` keeps the UI slim for Phase 3; a richer username picker is Phase-3+. The `Contacts` page offers a search-based picker, which is the primary way users will open chats.

- [ ] **Step 31.2: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/Sidebar.tsx \
        docs/superpowers/plans/2026-04-21-phase3-friends.md
git commit -m "feat(web): sidebar Personal tab offers New personal chat action"
```

---

## Task 32: Full Phase 3 sweep + E2E smoke

**Files:** none

- [ ] **Step 32.1: Full test run**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: all domain + integration tests green.

- [ ] **Step 32.2: Frontend build**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 0 errors.

- [ ] **Step 32.3: Commit marker**

```bash
git commit --allow-empty -m "chore: Phase 3 end-to-end smoke green"
```

---

## Phase 3 completion checklist

- [x] `FriendRequest` entity + `FriendRequestStatus` enum with behavior tests
- [x] `Friendship` entity with canonical unordered-pair ordering
- [x] `UserBlock` entity (asymmetric)
- [x] Three new authorization rules (`CanSendFriendRequest`, `CanPostInPersonalChat`, `CanOpenPersonalChat`) with unit tests
- [x] Three EF Core configurations (`friend_requests`, `friendships`, `user_blocks`) + migration `AddFriendsAndBlocks` with snake-case filtered unique index
- [x] REST: `POST/GET/DELETE /api/friend-requests/*`, `GET/DELETE /api/friends/*`, `GET /api/users/search`, `POST/DELETE /api/users/{userId}/block`, `POST /api/personal-chats/open` (10 endpoints)
- [x] Block side effects: delete `Friendship`, cancel pending `FriendRequest`s both ways
- [x] Personal channel (existing `Channel.CreatePersonal`) opened idempotently with two `ChannelMember` rows
- [x] `ChatHub.SendMessage` gates personal-chat posting on friendship + no-block
- [x] `FriendsEventBroadcaster` emits `FriendRequestReceived`, `FriendRequestDecided`, `FriendRemoved`, `Blocked`
- [x] Integration tests: friend request full lifecycle, block cancels friendship + pending requests, personal-chat open + post, realtime friend + block events
- [x] FE: `/contacts` page with friends / incoming / outgoing, `SendFriendRequestModal` with user search, sidebar Personal-tab action, realtime invalidation of friends + friend-requests queries

## What is deferred to later phases

- **Message attachments** (files + images) — Phase 4 (upload/download, ref-counted unlink, paste/drop).
- **Edit / delete-of-own-message via UI** (server endpoints exist since Phase 2) — Phase 4.
- **Presence heartbeats, active sessions, ForceLogout, unread counts** — Phase 5.
- **Rate limiting tuning, GlobalHubFilter, AuditLog admin surface, security headers, prod Docker** — Phase 6.
