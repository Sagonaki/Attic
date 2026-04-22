# Attic — Build Journey, Phase by Phase

A chat server built from nothing in 17 phases. Each phase starts from a working
baseline, ships one vertical cut of behavior (backend + frontend + tests), and
is merged with green tests before the next begins.

This doc is a compressed narrative of what shipped in each phase, **the actual
problems we ran into**, and how we solved them. Phases 11–17 are the
performance journey and are covered in more depth — they're also where most of
the interesting decisions live.

## Stack

- **API:** .NET 10, ASP.NET Core, SignalR, EF Core 10 + Npgsql, FluentValidation, xUnit v3
- **Infra:** PostgreSQL 17, Redis 7/8, .NET Aspire 13.x orchestration (Podman/Docker)
- **Web:** React 19, TanStack Query v5, React Router v6, `@microsoft/signalr` v8, Tailwind 4, shadcn/ui + Radix + lucide-react + sonner
- **Testing:** xUnit for unit/integration, Playwright for E2E, NBomber for load

---

## Phase 1 — Vertical Slice

**Goal:** Prove the plumbing end-to-end: cookie auth, one hardcoded public
channel, send text messages over SignalR with paginated history, React shell
wired to auth + realtime.

**Key architectural choices:**
- Custom cookie-session auth (server-side `Session` table as source of truth,
  not a JWT). Makes session revocation trivial later — just delete the row.
- Keyset pagination on message history (`WHERE id < @before ORDER BY id DESC`),
  not offset. Avoids cursor drift as new messages arrive.
- React wraps `@microsoft/signalr` in a thin hook that pushes inbound events
  directly into TanStack Query's cache — UI state stays in one place.

**Problems and fixes:**
- The Aspire `AddPostgres` connection string drops DbContext pooling options
  by default. Fix: register the context non-pooled so we can attach a
  DI-resolved `SaveChangesInterceptor` later (comes up again in Phase 6).

**Outcome:** 29 tests green. Foundation laid for every subsequent phase.

---

## Phase 2 — Rooms, Members, Invitations, Moderation

**Goal:** Replace the hardcoded lobby with real rooms: public + private kinds,
a catalog, join/leave, invitations, role management (owner/admin/member),
bans, and admin message deletion.

**Key architectural choices:**
- `AuthorizationRules` lives in the domain layer as **pure functions** —
  `CanPostInChannel(member)`, `CanDeleteMessage(msg, userId, member, kind)`.
  Unit-tested without any EF or SignalR plumbing. The hub and REST endpoints
  load the minimal rows and call into these rules. Rules never talk to the DB.
- SignalR group topology: one group per channel (`Channel_{id}`), one per user
  (`User_{id}`), one per session (`Session_{id}`). Everything fans out
  through groups — no `Clients.Users(...)` anywhere.

**Problems and fixes:**
- Banning a member from a channel has to both (a) flip `BannedAt` and (b) kick
  the live connection. Initially we did (b) via `Clients.User(id).SendAsync`;
  that races if the banned user has multiple tabs. Fix: broadcast to the
  user's group; each connected tab handles it independently.
- `ChannelMember` got a `HasQueryFilter(cm => cm.BannedAt == null)`. Call
  sites that need to see banned rows (authorization checks) use
  `IgnoreQueryFilters()` explicitly. Bugs in Phase 3 and again in Phase 14 came
  from forgetting this — captured as a repeatable pattern.

---

## Phase 3 — Friends, Blocks, Personal Chats

**Goal:** Bilateral friend requests, asymmetric user blocks, and 1:1 personal
chats gated by friendship + no-block.

**Key architectural choices:**
- `Friendship` is stored as a canonical unordered pair `(UserAId < UserBId)`.
  One row represents both sides; no dual-insert, no drift possible.
- Personal chats **reuse** the `Channel` entity with `Kind = Personal`, two
  `ChannelMember` rows, `Name = null`, `OwnerId = null`. This means every
  message, edit, delete, and broadcast path works identically for rooms and
  personal chats — no branching in the hub.
- Blocking removes the `Friendship`, cancels pending `FriendRequest`s both
  ways, but **keeps** the `Channel` around read-only. History stays visible;
  new sends are denied by `CanPostInPersonalChat(areFriends, hasBlock)`.

**Problems and fixes:**
- A user could send a friend request to themselves, leading to a degenerate
  canonical pair `(x, x)`. Fix: explicit rule in `CanSendFriendRequest`; unit
  test for it in the domain layer.
- Realtime: a `FriendRemoved` event must fire for *both* users, not just the
  actor. Easy miss — caught by an integration test that subscribes two hub
  connections and asserts both receive the event.

---

## Phase 4 — Attachments, Edit, Reply-To, Paste/Drop

**Goal:** End-to-end file/image attachments (streamed upload, content-addressable
storage), edit with `(edited)` marker, reply-to with composer reply bar, paste
and drop in the chat input.

**Key architectural choices:**
- **Content-addressable storage:** upload streams through `System.IO.Pipelines`
  to a temp file, SHA-256-hashed, atomic-rename into
  `/data/attachments/{yyyy}/{mm}/{dd}/{sha256}.bin`. Two uploads of identical
  bytes share one on-disk file; multiple `Attachment` rows point to it.
- Attachments start **unbound** (`MessageId IS NULL`). `SendMessage` binds them
  in the same transaction as the message insert. Two background sweepers clean
  up: one deletes unbound rows older than 24h, another unlinks the disk file
  when no row references it.
- Access control on download: requestor must be a non-banned member of the
  channel the attachment's message lives in. Check on every download.

**Problems and fixes:**
- Initially we bound attachments in two round-trips (message insert, then
  attachment update). A concurrent sweep could delete the unbound attachment
  between the two. Fix: bind inside the same `SaveChangesAsync` as the message
  insert, in a single transaction.

---

## Phase 5 — Presence, Unread Counters, Active Sessions, Account Deletion

**Goal:** Live presence (online/afk/offline via per-tab heartbeats), per-channel
unread counters, active-sessions screen with revoke + `ForceLogout`, and
account deletion that transactionally cascades.

**Key architectural choices:**
- **Presence:** `IPresenceStore` abstraction (Redis hash-per-user in prod,
  in-memory for tests). Each hub connection writes
  `{userId, connectionId, state: active|idle, at: ms}` on `Heartbeat`. A
  `PresenceHostedService` with a 1s `PeriodicTimer` scans a dirty set and
  derives per-user state (`online`/`afk`/`offline`). Broadcasts
  `PresenceChanged(userId, state)` only to interested subscribers: friends +
  channel co-members.
- **Unread counters** (v1): every `SendMessage` fires `UnreadChanged(channelId, newCount)`
  for every channel member except the sender. The count is computed by
  `SELECT COUNT(*) FROM messages WHERE channel_id = ? AND id > lastReadId`.
  This worked fine at spec scale but became the bottleneck in Phase 12.

**Problems and fixes:**
- First version of the derivation loop scanned **every** user every tick —
  wasteful. Fix: write a `dirty` Redis set on every heartbeat; derivation loop
  iterates the dirty set and clears it, so cost is O(active users) not O(all).
- `Heartbeat` initially accepted any string; a malformed client could write
  garbage states. Fix: reject anything that isn't `"active"` or `"idle"` at
  the hub entry point.

---

## Phase 6 — Hardening

**Goal:** "Feature-complete" → "production-ready." Audit log, hub filter, rate
limiting, security headers, reconnect catch-up.

**Key architectural choices:**
- `AuditLog` is populated by a `SaveChangesInterceptor` that inspects the
  `ChangeTracker` for tracked admin actions (ban, unban, role change, channel
  delete, admin message delete, session revoke, account delete). Log row and
  business write land in the same `SaveChangesAsync` — no async drift between
  state and audit trail.
- `GlobalHubFilter` wraps every hub invocation in try/catch, logs with a
  correlation id, and returns a uniform `{ok, code, message}` on failure.
  Clients never see unhandled exceptions.
- Rate limiting: built-in ASP.NET Core `RateLimiter` middleware handles REST
  (fixed-window 5/min on auth, sliding-window 60/min on upload). Hub has a
  custom per-user sliding-window `HubRateLimiter` (singleton, in-memory).

**Problems and fixes:**
- DbContext pooling (from Phase 1) collides with DI-resolved interceptors. The
  `SaveChangesInterceptor` needs scope access for the current user id, so the
  context has to be non-pooled. We accept a small per-request scope cost in
  exchange for a clean audit implementation.
- Kestrel's defaults cap request body at 30 MB — attachments cap at 20 MB but
  multipart overhead pushed some uploads over. Fix:
  `MaxRequestBodySize = 25 * 1024 * 1024` + `MaxConcurrentConnections = 1000`
  + `MaxConcurrentUpgradedConnections = 2048`.

---

## Phase 7 — Frontend Modernization (shadcn/ui + dark mode)

**Goal:** Raise the SPA from "minimal Tailwind" to a cohesive, accessible UI.
Adopt shadcn/ui primitives (copy-paste sources, Radix underneath),
lucide-react icons, sonner toasts, design-token CSS variables, light/dark
theme switcher.

**Key architectural choices:**
- shadcn/ui components live under `src/components/ui/` as committed source
  files, not a NuGet-style package. Upgrades are explicit diffs we read and
  accept.
- Design tokens as CSS custom properties in `index.css`, wired to Tailwind 4
  via its `@theme` directive.
- `<ThemeProvider>` toggles `.dark` on `<html>`; persisted to `localStorage`.

**Problems and fixes:**
- Tailwind 4 is a breaking change from 3 — the config moved from JS to CSS
  `@theme`. Had to rewrite the content globs and a handful of utility class
  names.
- Some Radix primitives render into portals; integration with our existing
  focus-trap logic required pulling the wrapping `Dialog` into the tree at
  the top level, not inside the feature components.

---

## Phase 8 — Fixes + UX Improvements

**Goal:** Post-Phase-7 punch list: forgot-password flow, profile page for
password change, realtime friend-request/invitation acceptance propagation,
personal-chat sidebar showing the other member's username, move active
sessions into the profile dropdown, plus four bug fixes.

**Key architectural choices:** None deep — this was a polish sprint. Each
improvement reused existing primitives (broadcasters, hooks, UI components).

**Problems and fixes:**
- "Forgot password" couldn't email anything in dev, so it writes the new
  password to the **server console** as a one-off dev affordance. Flagged in
  the spec as a production task.

---

## Phase 9 — Schema Hardening

**Goal:** A schema audit after Phase 8 found missing foreign keys, missing
indexes, and ints-masquerading-as-enums without `CHECK` constraints. Fix all
of them in one migration.

**Key architectural choices:**
- `OnDelete` picks: `Cascade` where the dependent row has no standalone
  meaning (sessions without a user, channel_reads without a channel),
  `Restrict` where the app explicitly orchestrates deletion order
  (`channels.owner_id` — account-delete hard-deletes owned channels' dependents
  first, then the channels, then the user).
- Partial indexes for nullable columns where the query is "WHERE X IS NOT NULL"
  (e.g. `channels.owner_id`). Saves index space on the nulls.
- `CHECK ck_<table>_<column>_enum (<column> IN (0, 1, 2))` on every int-backed
  enum column. A stray integer write can't corrupt state anymore.

**Problems and fixes:**
- A `ux_channels_name_not_personal` unique index (name not null, not personal)
  started tripping 500s on concurrent catalog creates. Surfaces again in
  Phase 11 under load test — harness had to serialize the first create.
- Recomputed index sizes on a seeded DB; one partial index shaved ~40%
  vs. the full equivalent.

---

## Phase 10 — E2E Playwright

**Goal:** A `tests/Attic.Web.E2E/` project with three golden-path scenarios
running real browsers against the live Aspire AppHost. Doubles as a manual
smoke harness.

**Key architectural choices:**
- `E2E_BASE_URL` env var — tests adapt to whatever port Aspire assigned that
  run.
- Chromium only for MVP. Cross-browser in CI is a later call.
- Developer runs Aspire in a separate terminal; tests don't own the
  runtime. Mirrors the CI approach in the spec.

**Problems and fixes:**
- Tests initially tried to register + login inside a `beforeAll`, which Playwright
  runs once per worker. Races on shared state. Fix: `registerFreshUser` helper
  that creates a uniquely-named user per test.

---

## Phase 11 — Hybrid Load Testing

**Goal:** Validate the spec's **300 concurrent users** target. Add a protocol-level
NBomber harness + a Playwright 30-context browser stress test + a live monitoring
loop via Aspire's MCP server.

**Key architectural choices:**
- NBomber virtual user = one HTTP cookie jar + one persistent `HubConnection`,
  pre-registered so the load window doesn't double as a registration storm.
- Acceptance gate: `> 1%` error rate fails the run (exit 1). Enforced in
  `Program.cs`.
- Aspire MCP (`list_resources`, `list_structured_logs`, `list_traces`) as the
  live monitoring surface — cheaper than scraping `podman logs` by hand.

**Problems and fixes:**
- First smoke: 300 concurrent users all POSTed `/api/channels` with the same
  name. Phase 9's `ux_channels_name_not_personal` unique index turned that
  race into 500s. Fix in the harness: `user[0]` pre-creates the shared channel
  serially; the rest take the join path.
- `ChatScenarioOptions.InstanceNumber` vs `ThreadNumber` in NBomber 6.3 —
  `ThreadNumber` was deprecated but still compiled. Silent picking of the
  wrong one made virtual-user indexes collide. Fix: explicit `InstanceNumber`
  only.

**Outcome:** Harness stable at 50-user smoke. 300-user run exposed
`send_message p95 ≈ 809 ms` — above the spec's 250 ms target. Queued for Phase 12.

---

## Phase 12 — Redis-Backed Unread Counters

**Problem:** Load test showed `send_message` p95 = 809 ms at 50 users. The
hub's `SendMessage` did `SELECT COUNT(*) FROM messages WHERE channel_id = ?
AND id > lastReadId` **per channel member** for the `UnreadChanged` fan-out.
At ~N COUNT queries per send × 300 members = 300 extra DB round-trips per
send. Postgres was the obvious bottleneck.

**Fix:** Replace the per-send COUNT with a Redis-backed counter. New
`IUnreadCountStore` abstraction:

- `RedisUnreadCountStore` uses `INCR unread:user:{user}:channel:{channel}` per
  member (O(1) per member, atomic).
- `InMemoryUnreadCountStore` with `ConcurrentDictionary.AddOrUpdate` +
  `Interlocked.Increment` for tests.
- `MarkRead` calls `ResetAsync` (SET to 0).
- Registered as singleton so all hub scopes share the multiplexer.

**Outcome (50-user smoke):** `send_message` p95 809 → 245 ms. Passed the spec
target at small scale. Onward to the real 300-user test.

---

## Phase 13 — Parallelize Fan-out (Task.WhenAll)

**Problem:** 300-user run. Still failing — 25% OK rate on send. The hub was
doing the Redis INCR + `Clients.Group(User_X).SendAsync` for each member
**serially** in a `foreach` loop. At 300 members × small await each, that's
hundreds of ms of sequential latency per send while all 300 virtual users are
pounding the hub.

**Fix:** One-line change —

```csharp
var fanoutTasks = memberIds.Select(async memberId => {
    var newCount = await unreadCounts.IncrementAsync(memberId, channelId, ct);
    await Clients.Group(GroupNames.User(memberId))
        .SendAsync("UnreadChanged", channelId, (int)newCount, ct);
});
await Task.WhenAll(fanoutTasks);
```

**Outcome:** 300-user run: 25% → 34% OK rate, p95 226s → 201s. Better, still
failing. Something bigger is going on.

---

## Phase 14 — Pool + Threads + Query Reduction

**Problem:** Dug into the 300-user failure. Several suspected bottlenecks:

1. ThreadPool starvation (default min threads = CPU count; pool grows slowly).
2. Npgsql default pool = 100; at 300 concurrent sends each doing ~5 queries,
   we're asking for 1500 concurrent connections.
3. `SendMessage` was doing ~5-8 distinct queries per call — a lot.

**Fixes (all three together):**

- **ThreadPool:** `ThreadPool.SetMinThreads(200, 200)` at `Program.cs` top.
- **Npgsql pool:** bump `Maximum Pool Size` to 300 via
  `NpgsqlConnectionStringBuilder` applied to the connection string in
  `AddAtticDbContext`.
- **Query reduction** (EF Core LINQ only — no raw SQL):
  - Combine the per-channel member lookup + the `Channels.Kind` read into one
    `LEFT JOIN LATERAL` projection. Reduces 2 queries → 1.
  - Combine the sender-username lookup + the fan-out member roster into one
    `ChannelMembers JOIN Users` query. Reduces 2 queries → 1. Banned peers
    filtered in memory (preserves prior semantics; sender's own row always
    included via `IgnoreQueryFilters`).
  - Skip the post-save attachment re-select — project `AttachmentDto[]`
    directly from the tracked entities we already have.

Hot-path query count: **5-8 → 3** per send.

**Outcome: worse.** 300-user run regressed: send OK rate 10.7% → 6.5%. p95 jumped.
Very surprising — the change should have helped.

**Root cause (found in Phase 15):** Postgres `max_connections = 100`. Our
client-side pool bump to 300 was *meaningless* — the server was rejecting
connections with `FATAL: sorry, too many clients already`. Every prior run
had been hitting this cap too; Phase 14's slightly slower queries made the
queue-up-and-timeout problem strictly worse because queries held connections
longer.

**Lesson:** Always check the server-side cap, not just the client-side pool.

---

## Phase 15 — Postgres max_connections = 400 (one-line Aspire fix)

**The real fix.** `podman logs postgres-*` during a failed Phase 14 run showed
dozens of lines:

```
FATAL: sorry, too many clients already
```

Postgres's compiled default is 100, and Aspire's `AddPostgres` doesn't
override it. Fix: one line in the AppHost:

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("attic-pg")
    .WithArgs("-c", "max_connections=400")   // ← this
    .AddDatabase("attic");
```

`max_connections` is a server startup arg, not a compile-time default —
changing it requires a container restart but not a volume wipe.

**Outcome:**

| step | Phase 14 OK | **Phase 15 OK** |
|---|---|---|
| send_message | 6.5% | **34.0%** |
| heartbeat | 24.7% | **60.9%** |

300-user run: zero `too many clients already` in Postgres log. Pool peaked at
302 / 400 with only 1–6 active queries at any moment — **Postgres is idle-bound,
not the bottleneck anymore**. But still failing the 1% error gate.

Latency pattern: `send_message` min = 30019 ms. Every successful send takes
at least 30 seconds. That's SignalR's `HubConnection.InvokeAsync` default
timeout — the server is holding the hub invocation open for 30s+.

Where? The hub was still awaiting the Redis backplane broadcast + per-member
INCR + `SendAsync` in-line, serially for each member. Even parallelized via
Phase 13's `Task.WhenAll`, 300 concurrent hub invocations each doing 300
await-able broadcasts funnelled through one Redis multiplexer writer pipe
meant a long queue.

---

## Phase 16 — Background Fan-out Queue (the big unlock)

**Problem from Phase 15:** The hub method's latency is dominated by
backplane/Redis awaits, not by persistence. The hub holds open the SignalR
invocation until fan-out completes. At 300 concurrent users × 300 fan-out
targets, the awaits pile up.

**Fix:** Move fan-out off the hub invocation path entirely.

- **`MessageFanoutQueue`** — `System.Threading.Channels.Channel<T>`-backed
  unbounded single-reader FIFO. Preserves per-channel order (enqueue order
  wins the race because DB commit happens first).
- **`MessageFanoutService`** — a `BackgroundService` that drains the queue.
  Broadcasts `MessageCreated` + per-member `IncrementAsync` +
  `UnreadChanged` — exactly the work the hub used to do inline.
- **`ChatHub.SendMessage`** — after `SaveChangesAsync`, builds the `MessageDto`
  and `TryEnqueue`s one work item. Returns immediately.

Client observable effect: sender's own client sees its own `MessageCreated`
broadcast with a small (≤ms) async delay instead of synchronously. Fine for
chat UX — clients optimistically echo anyway.

**Outcome:**

| step | Phase 15 | **Phase 16** |
|---|---|---|
| send_message OK rate | 34.0% | **100.0%** |
| send_message p50 | ~30s | **106 ms** |
| send_message p95 | 198 s | **909 ms** |
| `[loadtest]` | FAIL | **PASS** (< 1% error gate) |

Redis was doing ~158k ops/s (24M PUBLISH + 23.5M INCR over 5 min) without
breaking a sweat. Postgres peaked at 302 / 400 connections. API CPU was 7+
cores at peak, 86% mean.

**Phase 16 passed the spec's hard gate.** The softer p95 < 250 ms target was
still open (at 909 ms). The full journey from Phase 13:

| phase | send OK rate | send p50 | send p95 |
|---|---|---|---|
| 13 | 10.7% | 30 s | 198 s |
| 14 | 6.5% | 30 s | — (worse) |
| 15 | 34.0% | 30 s | 198 s |
| **16** | **100.0%** | **106 ms** | **909 ms** |

---

## Phase 17 — Chasing p95 < 250 ms

Phase 16 passed the acceptance gate but left `p95 = 909 ms` on the table.
Phase 17 is an evidence-gated sequence of optimizations, each measured with a
full 300-user instrumented run (`dotnet-counters` on the API process,
Redis `INFO commandstats` before/after, `pg_stat_activity` sampled every 5s).

### Checkpoint 1 — MessagePack hub protocol

**Change:** Add `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` on server
and load-test client. Protocol is negotiated per-connection — clients that
only speak JSON fall back automatically.

**Result:** p50 **106 → 20 ms**, p95 **909 → 708 ms**. GC alloc peak **1472
→ 864 MB / 2s**. Working set **6.8 → 4.2 GB**. Payloads shrunk, (de)serialization
cheapened, GC pressure halved.

### Checkpoint 2 — ObjectPool + ArrayPool for the hot path

**Evidence:** Even after MessagePack, `dotnet-counters` showed 940 MB/2s of
allocations, 1289 lock contentions/2s. Driver: per-send allocation of
`MessageFanoutWorkItem` + per-send `List<Guid>` member-ID list.

**Change:**
- `MessageFanoutWorkItem` converted from `record` → mutable pooled class with
  a `Reset()` method. Registered
  `ObjectPool<MessageFanoutWorkItem>` via `DefaultObjectPoolProvider`.
- Member IDs now rented from `ArrayPool<Guid>.Shared` — paired with an
  explicit `MemberCount` since `Rent(n)` can return an array `>= n`.
- Hub rents → populates → enqueues. Service drains → broadcasts → releases
  array + returns item in `finally`. Defensive guard: if
  `fanoutQueue.TryEnqueue` returns false (shutdown race), hub releases
  everything instead of leaking.

**Result:** p50 **20 → 16 ms**, p95 **708 → 338 ms**. Lock contention doubled
to 2663/2s (ObjectPool has internal bucket locks), but wall-clock p95 still
dropped 52%.

### Checkpoint 3 — Parallel.ForEachAsync drain

**Evidence:** The drain loop was doing
`new Task[memberCount] + Task.Run(async () => ...) × memberCount + Task.WhenAll`.
At ~300 members × hundreds of sends/s, that's ~300k closure allocations per
second pressuring gen-0. GC pause peak = 172 ms.

**Change:** Replace the manual task array with
`await Parallel.ForEachAsync(new ArraySegment<Guid>(memberIdsArray, 0, memberCount), options, async (memberId, ct) => {...})`.
Uses a fixed-size worker partitioner, bounded at `MaxDegreeOfParallelism = 32`.

**Result:** p50 **16 → 8 ms**, p95 **338 → 343 ms** (basically unchanged).
Allocation peak **940 → 402 MB / 2s**, threadpool queue peak **423 → 58**.
*But* lock contention spiked to **4947/2s** — 32 workers now serialize
through one shared partitioner enumerator + one shared Redis multiplexer
writer pipe. Median case faster, tail unchanged.

### Checkpoint 4 — Redis IBatch for unread INCR

**Evidence:** The parallel drain still made N individual `INCR` calls through
the single `IConnectionMultiplexer` writer pipe, each acquiring its lock.
At ~19k INCR/s × N members, the pipe is the funnel. `StackExchange.Redis`
has `IDatabase.CreateBatch()` → `batch.StringIncrementAsync(...)` × N →
`batch.Execute()` — queues commands + dispatches them back-to-back without
yielding between.

**Change:**
- New `IUnreadCountStore.IncrementManyAsync(IReadOnlyList<Guid> userIds, Guid channelId, CancellationToken)`.
- `RedisUnreadCountStore`: builds batch, queues N `StringIncrementAsync`,
  synchronous `batch.Execute()` (NOT awaited — `Execute()` returns void),
  `Task.WhenAll(tasks)` collects the per-command replies.
- `InMemoryUnreadCountStore`: loop using the existing `AddOrUpdate` +
  `Interlocked.Increment` idiom.
- Drain loop refactored to two phases: **Phase 1** batched INCRs across the
  whole roster (one lock acquisition for N commands); **Phase 2** parallel
  `UnreadChanged` broadcasts via `Parallel.ForEachAsync` using the returned
  counts. SignalR's internal backplane PUBLISH stays per-message because the
  backplane doesn't expose a batch seam.

**Result:** p50 **8 → 9 ms** (basically unchanged), p95 **343 → 285 ms**.
Lock contention **4947 → 2021 / 2s**. GC pause peak **171 → 147 ms**.
The biggest single-step p95 win since MessagePack.

### Phase 17 final

| metric | Phase 16 baseline | Phase 17 final | Δ |
|---|---|---|---|
| send p50 | 106 ms | **9 ms** | **-92%** |
| send p95 | 909 ms | **285 ms** | **-69%** |
| heartbeat p95 | 88 ms | **19 ms** | -78% |
| GC alloc peak (MB/2s) | 1472 | 345 | -77% |
| working set peak (MB) | 6825 | 3843 | -44% |
| threadpool queue peak | 878 | 196 | -78% |
| lock contention peak / 2s | 1289 | 2021 | — |
| Redis PUBLISH in 5 min | 24.1 M | 6.78 M | -72% |
| Redis INCR in 5 min | 23.5 M | 6.58 M | -72% |

**We're 35 ms above the 250 ms sub-target.** The tail is dominated by four
Gen2 GC collections (up to 147 ms pause each): a send that coincides with a
pause takes `~135 ms normal + 147 ms pause = ~282 ms` — exactly the observed
p95. Closing that further is follow-up work (GC env-var tuning, MessageDto
pooling, or reshaping the load workload to multi-channel which matches the
spec's intent better).

**The 1% error-rate spec gate passes convincingly at 100% step success.**

---

## What the perf journey taught us

1. **Don't trust one layer's limits — check the whole stack.** Phase 14
   bumped Npgsql's pool to 300 and things got *worse*. The cap was actually
   Postgres's `max_connections = 100`, two containers deep. A single
   `.WithArgs("-c", "max_connections=400")` in the AppHost was the unlock.
   Always `podman logs <db>` when pool weirdness shows up.

2. **Moving work off the request/invocation path is usually the biggest win.**
   Phase 16 (background fan-out queue) took us from 34% OK rate to 100% OK
   rate in one change. Every phase after that was polishing.

3. **Measure, then act. Then measure again.** Every Phase 17 change was
   preceded by `dotnet-counters` output + Redis commandstats delta + Postgres
   activity sampling. "I think this is the problem" is not evidence.

4. **The obvious optimization isn't always the right one.** Phase 17's plan
   called for sharding the fan-out queue by `ChannelId` (Task 4). When we
   looked at the load-test, all 300 users were in one channel — sharding
   would be a no-op. We rescoped Task 4 mid-flight to `Parallel.ForEachAsync`
   and added a Task 4b for Redis `IBatch`. Plans are hypotheses.

5. **Lock contention is a second-order thing.** Chasing p95 we often
   *increased* lock contention while *decreasing* wall-clock latency. CP3
   doubled contention but halved p50. What matters is end-to-end, not any
   single counter.

6. **GC pauses are the floor.** Once the hot path is clean, the tail becomes
   "how often does a send coincide with a Gen2 pause." Further wins come from
   reducing allocation volume (Phase 17 tasks) or accepting the floor and
   shaping workload to hit it less often.

## What each phase consists of (the repeating pattern)

Looking across all 17 phases, the same rhythm emerges:

1. **Plan doc** under `docs/superpowers/plans/YYYY-MM-DD-phaseN-*.md` — goal,
   architecture, tech stack, file structure, task list with checkpoints.
2. **Subagent-driven implementation** — one task per subagent, full task text
   in the prompt, two-stage review (spec compliance first, code quality
   second) after each task.
3. **Checkpoint commits** — build 0/0, tests green.
4. **Instrumented validation** (perf phases only) — load test + counters
   + live DB/Redis sampling.
5. **Merge with `--no-ff`** so the phase boundary stays visible in `git log`.
6. **Outcome doc** (perf phases only) appended to the plan, under
   `tests/.../docs/phaseN-results.md`.

The test suites grew with each phase: from 29 tests (Phase 1) to 117 Domain +
71 Integration (Phase 17). One pre-existing timing race in
`SessionsFlowTests.Revoke_other_session_fires_ForceLogout_on_that_session_group`
(added in Phase 5 at `2a110cc`) flakes under parallel stress but passes in
isolation — documented and left alone.

## Where to look

- **Per-phase plans:** `docs/superpowers/plans/`
- **Original design spec:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md`
- **Phase 17 result breakdown:** `tests/Attic.Web.LoadTests/docs/phase17-results.md`
- **Load-test harness:** `tests/Attic.Web.LoadTests/`
- **E2E scenarios:** `tests/Attic.Web.E2E/`
- **Aspire MCP monitoring walk-through:** `tests/Attic.Web.LoadTests/docs/monitoring.md`

## License

Same as the project — see `LICENSE` at the repo root.
