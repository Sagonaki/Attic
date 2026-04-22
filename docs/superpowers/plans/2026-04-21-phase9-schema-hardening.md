# Attic Phase 9 — Schema Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development.

**Goal:** Fix the HIGH and MEDIUM findings from the Phase 8 schema audit: add missing foreign-key constraints on every relationship, plug the `sessions.token_hash` unique-index gap (currently seq-scanned on auth), add missing indexes on `sessions.expires_at`, `channels.owner_id` (partial, where not null), and `audit_logs.target_user_id`/`target_message_id`. Add CHECK constraints on enum-as-int columns (`channel_invitations.status`, `friend_requests.status`, `channel_members.role`, `channels.kind`) so a stray integer write can't corrupt state.

**Architecture:** Every fix ships via a single EF Core migration. Entity configurations under `src/Attic.Infrastructure/Persistence/Configurations/` declare the relationships using `.HasOne<T>().WithMany().HasForeignKey(...)` — no navigation properties are added (the domain model stays anemic on relationships; EF only needs the FK definition to emit `REFERENCES ... ON DELETE ...`). `OnDelete` semantics are chosen conservatively: `Cascade` where the dependent row has no meaning without the principal (sessions without a user, channel_reads without a channel, friendships without either endpoint), `Restrict` where the application explicitly orchestrates deletion order (channels.owner_id — account-delete hard-deletes owned channels' dependents first). Indexes use partial-index predicates when the column is nullable and the intent is "WHERE IS NOT NULL" (channels.owner_id). CHECK constraints use `ck_<table>_<column>_enum` naming and are unquoted snake_case.

**Tech Stack:** No new dependencies. EF Core 10 + Npgsql.

**Spec reference:** Addresses audit findings from Phase 8 post-merge. Not changing the data-model narrative in `docs/superpowers/specs/2026-04-21-attic-chat-design.md`; just adding the DB-level enforcement the app already assumes.

---

## Prerequisites

- All 188 tests currently pass. No regression allowed.
- Phase 5's account-delete flow explicitly hard-deletes dependents in a correct topological order. Adding FK constraints with `Restrict` on users must not break that order — we verify after the migration.
- EF Core's `OnDelete(DeleteBehavior.Cascade)` emits `ON DELETE CASCADE` in PostgreSQL; `Restrict` emits `ON DELETE NO ACTION` with deferred checking, which will reject the delete if any dependent row exists at commit.

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-9`.
- Domain tests: 117. Integration tests: 71.
- Frontend lint + build clean.
- Podman running, `DOCKER_HOST` set.

---

## File structure additions

```
src/Attic.Infrastructure/Persistence/
├── Configurations/
│   ├── ChannelConfiguration.cs                         (modify)
│   ├── ChannelMemberConfiguration.cs                   (modify)
│   ├── ChannelInvitationConfiguration.cs               (modify)
│   ├── MessageConfiguration.cs                         (modify)
│   ├── AttachmentConfiguration.cs                      (modify)
│   ├── SessionConfiguration.cs                         (modify)
│   ├── FriendshipConfiguration.cs                      (modify)
│   ├── FriendRequestConfiguration.cs                   (modify)
│   ├── UserBlockConfiguration.cs                       (modify)
│   ├── ChannelReadConfiguration.cs                     (modify)
│   └── AuditLogConfiguration.cs                        (modify)
└── Migrations/
    └── XXXXXXXXXXXXXX_SchemaHardening.cs               (generated)
```

No new entity configurations. No contracts or API changes.

---

## Relationship matrix (FKs to add)

| Table | Column | References | OnDelete | Rationale |
|---|---|---|---|---|
| `channels` | `owner_id` | `users.id` nullable | Restrict | Personal channels have no owner (null). Account-delete manually hard-deletes owned channels before the user row. |
| `channel_members` | `channel_id` | `channels.id` | Cascade | A member row is meaningless without its channel. |
| `channel_members` | `user_id` | `users.id` | Restrict | Account-delete explicitly removes memberships first. |
| `channel_invitations` | `channel_id` | `channels.id` | Cascade | An invitation has no meaning without a channel. |
| `channel_invitations` | `inviter_id` | `users.id` | Restrict | Explicit cleanup in account-delete. |
| `channel_invitations` | `invitee_id` | `users.id` | Restrict | Explicit cleanup. |
| `messages` | `channel_id` | `channels.id` | Restrict | Messages survive soft-delete; channel hard-delete is only in account-delete which pre-removes messages. |
| `messages` | `sender_id` | `users.id` | Restrict | Messages preserve sender as "deleted user" via the soft-deleted User row. |
| `messages` | `reply_to_id` | `messages.id` (self) nullable | SetNull | If parent is (hypothetically) hard-deleted, child becomes orphan-reply. |
| `attachments` | `message_id` | `messages.id` nullable | SetNull | Orphan uploads are swept by `AttachmentSweeperService`; bound attachments lose their binding if the message is hard-deleted. |
| `attachments` | `uploader_id` | `users.id` | Restrict | Preserved via User soft-delete. |
| `sessions` | `user_id` | `users.id` | Cascade | Sessions die with their user. Account-delete explicitly deletes sessions first, but Cascade is a safety net. |
| `friendships` | `user_a_id` | `users.id` | Cascade | Same rationale. |
| `friendships` | `user_b_id` | `users.id` | Cascade | Same rationale. |
| `friend_requests` | `sender_id` | `users.id` | Cascade | Explicit cleanup, Cascade as safety. |
| `friend_requests` | `recipient_id` | `users.id` | Cascade | Same. |
| `user_blocks` | `blocker_id` | `users.id` | Cascade | Same. |
| `user_blocks` | `blocked_id` | `users.id` | Cascade | Same. |
| `channel_reads` | `channel_id` | `channels.id` | Cascade | Meaningless without channel. |
| `channel_reads` | `user_id` | `users.id` | Cascade | Meaningless without user. |
| `audit_logs` | `actor_user_id` | `users.id` | Restrict | Audit preserves actor; records survive account-delete via soft-delete. |

**Target-fields on audit_log (`target_user_id`, `target_channel_id`, `target_message_id`)** are deliberately NOT FK'd — the audit log needs to outlive the thing it recorded, so dangling references are fine.

## Indexes to add

- `sessions` — unique index on `(token_hash)`. Session auth queries by token hash — currently seq-scans.
- `sessions` — partial index on `(expires_at)` WHERE `revoked_at IS NULL` — drives expired-session cleanup queries.
- `channels` — partial unique index wouldn't make sense; a plain partial index on `(owner_id)` WHERE `owner_id IS NOT NULL` covers "list channels owned by X".
- `audit_logs` — index on `(target_user_id)` WHERE `target_user_id IS NOT NULL`.
- `audit_logs` — index on `(target_message_id)` WHERE `target_message_id IS NOT NULL`.

## CHECK constraints to add

- `channel_invitations` — `status IN (0,1,2,3)` (Pending/Accepted/Declined/Cancelled).
- `friend_requests` — `status IN (0,1,2,3)` (Pending/Accepted/Declined/Cancelled).
- `channel_members` — `role IN (0,1,2)` (Owner/Admin/Member).
- `channels` — `kind IN (0,1,2)` (Public/Private/Personal).

---

## Task ordering rationale

Two checkpoints:

- **Checkpoint 1 — Entity configurations (Tasks 1-6):** Update all 11 configurations with FK declarations, new indexes, and CHECK constraints. Each task groups related entities.
- **Checkpoint 2 — Migration + tests (Tasks 7-10):** Generate `SchemaHardening` migration, sanity-check SQL, run tests, fix anything exposed by the new FK enforcement, mark complete.

---

## Task 1: Channel + ChannelMember + ChannelInvitation FKs

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/ChannelMemberConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/ChannelInvitationConfiguration.cs`

- [ ] **Step 1.1: `ChannelConfiguration.cs` — owner FK + partial index + CHECK on kind**

Inside `Configure`, after the existing setup:

```csharp
        // FK: owner_id → users.id (nullable; personal channels have null owner). RESTRICT because
        // account-delete hard-deletes owned channels' dependents before the user.
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(c => c.OwnerId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channels_owner");

        // Partial index on owner_id for "my owned channels" lookups.
        b.HasIndex(c => c.OwnerId)
         .HasDatabaseName("ix_channels_owner")
         .HasFilter("owner_id IS NOT NULL");

        // CHECK: kind ∈ {0,1,2}.
        b.ToTable(t => t.HasCheckConstraint("ck_channels_kind_enum", "kind IN (0,1,2)"));
```

Add `using Attic.Domain.Entities;` if missing. If `ChannelConfiguration` already calls `b.ToTable(...)` elsewhere, note that multiple `ToTable` configuration chains are fine — EF merges them.

- [ ] **Step 1.2: `ChannelMemberConfiguration.cs` — channel + user FKs + role CHECK**

```csharp
        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(m => m.ChannelId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_members_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(m => m.UserId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channel_members_user");

        b.ToTable(t => t.HasCheckConstraint("ck_channel_members_role_enum", "role IN (0,1,2)"));
```

- [ ] **Step 1.3: `ChannelInvitationConfiguration.cs` — 3 FKs + status CHECK**

```csharp
        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(i => i.ChannelId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_invitations_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(i => i.InviterId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channel_invitations_inviter");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(i => i.InviteeId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channel_invitations_invitee");

        b.ToTable(t => t.HasCheckConstraint("ck_channel_invitations_status_enum", "status IN (0,1,2,3)"));
```

- [ ] **Step 1.4: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/ChannelMemberConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/ChannelInvitationConfiguration.cs \
        docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): FK + index + enum CHECK on channels, members, invitations"
```

Expected: 0/0.

---

## Task 2: Message + Attachment FKs

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/MessageConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/AttachmentConfiguration.cs`

- [ ] **Step 2.1: `MessageConfiguration.cs`**

Append inside `Configure`:
```csharp
        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(m => m.ChannelId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_messages_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(m => m.SenderId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_messages_sender");

        // Self-FK: reply_to_id is nullable; SET NULL if parent is deleted.
        b.HasOne<Message>()
         .WithMany()
         .HasForeignKey(m => m.ReplyToId)
         .OnDelete(DeleteBehavior.SetNull)
         .HasConstraintName("fk_messages_reply_to");
```

- [ ] **Step 2.2: `AttachmentConfiguration.cs`**

```csharp
        b.HasOne<Message>()
         .WithMany()
         .HasForeignKey(a => a.MessageId)
         .OnDelete(DeleteBehavior.SetNull)
         .HasConstraintName("fk_attachments_message");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(a => a.UploaderId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_attachments_uploader");
```

- [ ] **Step 2.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/MessageConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/AttachmentConfiguration.cs \
        docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): FKs on messages + attachments (with SET NULL on reply_to/message)"
```

---

## Task 3: Session FKs + unique token_hash + expires_at index

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/SessionConfiguration.cs`

- [ ] **Step 3.1: Extend the configuration**

Inside `Configure`, append:
```csharp
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(s => s.UserId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_sessions_user");

        // Auth lookup path: WHERE token_hash = @hash. Make it unique + indexed.
        b.HasIndex(s => s.TokenHash)
         .IsUnique()
         .HasDatabaseName("ux_sessions_token_hash");

        // Cleanup path: WHERE revoked_at IS NULL AND expires_at > now(). Partial on revoked to keep it tight.
        b.HasIndex(s => s.ExpiresAt)
         .HasDatabaseName("ix_sessions_expires_at")
         .HasFilter("revoked_at IS NULL");
```

Verify by reading the existing `SessionConfiguration.cs` first — Phase 1 may already have an index on `(user_id)` filtered by `revoked_at IS NULL`. Don't duplicate.

- [ ] **Step 3.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/SessionConfiguration.cs docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): sessions FK + unique token_hash index + expires_at partial index"
```

---

## Task 4: Friend graph FKs (Friendship, FriendRequest, UserBlock, ChannelRead)

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/FriendshipConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/FriendRequestConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/UserBlockConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/ChannelReadConfiguration.cs`

- [ ] **Step 4.1: `FriendshipConfiguration.cs`**

```csharp
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(f => f.UserAId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friendships_user_a");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(f => f.UserBId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friendships_user_b");
```

- [ ] **Step 4.2: `FriendRequestConfiguration.cs`**

```csharp
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.SenderId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friend_requests_sender");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.RecipientId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friend_requests_recipient");

        b.ToTable(t => t.HasCheckConstraint("ck_friend_requests_status_enum", "status IN (0,1,2,3)"));
```

- [ ] **Step 4.3: `UserBlockConfiguration.cs`**

```csharp
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(x => x.BlockerId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_user_blocks_blocker");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(x => x.BlockedId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_user_blocks_blocked");
```

- [ ] **Step 4.4: `ChannelReadConfiguration.cs`**

```csharp
        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(r => r.ChannelId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_reads_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_reads_user");
```

- [ ] **Step 4.5: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/FriendshipConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/FriendRequestConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/UserBlockConfiguration.cs \
        src/Attic.Infrastructure/Persistence/Configurations/ChannelReadConfiguration.cs \
        docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): FKs on friend graph + channel_reads + friend_requests status CHECK"
```

---

## Task 5: AuditLog FK + target indexes

**Files:**
- Modify: `src/Attic.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`

- [ ] **Step 5.1: Extend configuration**

```csharp
        // Actor FK — Restrict so audit survives soft-deleted users (tombstone remains reachable).
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(l => l.ActorUserId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_audit_logs_actor");

        // Target lookups.
        b.HasIndex(l => l.TargetUserId)
         .HasDatabaseName("ix_audit_logs_target_user")
         .HasFilter("target_user_id IS NOT NULL");

        b.HasIndex(l => l.TargetMessageId)
         .HasDatabaseName("ix_audit_logs_target_message")
         .HasFilter("target_message_id IS NOT NULL");
```

- [ ] **Step 5.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): audit_logs actor FK + target indexes"
```

---

## Task 6: Checkpoint 1 marker

- [ ] **Step 6.1: Full domain test run**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: 117 still passing (no domain-level behavior changed).

- [ ] **Step 6.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 9 Checkpoint 1 (EF configurations updated)"
```

---

## Task 7: Generate migration `SchemaHardening`

**Files:**
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/*_SchemaHardening.cs`

- [ ] **Step 7.1: Generate**

```bash
dotnet tool run dotnet-ef migrations add SchemaHardening \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 7.2: Sanity-check the idempotent SQL**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase9-hardening.sql

# Count new FKs:
grep -c "ADD CONSTRAINT fk_" /tmp/phase9-hardening.sql
# Expect 21 FKs.

# Count new CHECKs:
grep -c "ADD CONSTRAINT ck_" /tmp/phase9-hardening.sql
# Expect 4 CHECKs (channel_invitations, friend_requests, channel_members, channels).

# Count new indexes:
grep -cE "CREATE (UNIQUE )?INDEX (ux|ix)_" /tmp/phase9-hardening.sql
# A bunch — the ones we care about: ux_sessions_token_hash, ix_sessions_expires_at,
# ix_channels_owner, ix_audit_logs_target_user, ix_audit_logs_target_message.
grep -E "ux_sessions_token_hash|ix_sessions_expires_at|ix_channels_owner|ix_audit_logs_target_user|ix_audit_logs_target_message" /tmp/phase9-hardening.sql
```

All listed index names must appear. All filter clauses must use unquoted snake_case (no `"owner_id"` in filter strings).

If any filter reads `WHERE "Column"` (quoted) — STOP and report. The fix is to quote the column correctly in `.HasFilter(...)` with snake_case.

- [ ] **Step 7.3: Build solution + commit**

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Migrations docs/superpowers/plans/2026-04-21-phase9-schema-hardening.md
git commit -m "feat(infra): migration SchemaHardening (21 FKs, 4 CHECKs, 5 new indexes)"
```

---

## Task 8: Integration test run — enforce FK correctness

**Files:** none (verification).

The FKs may expose ordering bugs in account-delete or similar cascades. Run the full integration suite.

- [ ] **Step 8.1: Run**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests
```

Expected: 71 passing.

If any test fails with a FK violation (e.g. `violates foreign key constraint fk_...`), the most likely cause is `DeleteAccount` deleting in an order the new constraints disallow. Typical remediation:

- Ensure `messages` are deleted BEFORE `channels`.
- Ensure `attachments` with `message_id` set are deleted or have `message_id` nulled BEFORE those messages are deleted (the `SetNull` OnDelete should handle this automatically, but only if the constraint was actually created with `SET NULL`).
- `ChannelMembers` with `user_id = self` must be deleted BEFORE the user is soft-deleted if `fk_channel_members_user` uses `Restrict`. Phase 5's code already does this.

If a failure is genuinely a bug in Phase 5's deletion order, fix it in the `DeleteAccount` endpoint (not by relaxing the FK). Keep the commit message focused.

- [ ] **Step 8.2: If tests pass, commit marker**

```bash
git commit --allow-empty -m "chore: Phase 9 tests pass under new FK constraints"
```

If tests failed and needed fixing, commit the fix(es) separately before the marker.

---

## Task 9: Final smoke + project state

- [ ] **Step 9.1: Full run**

```bash
dotnet test
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 117 domain + 71 integration = 188 green. Frontend builds clean (no FE changes).

- [ ] **Step 9.2: Checkpoint 2 / phase marker**

```bash
git commit --allow-empty -m "chore: Phase 9 complete — schema hardening (FKs + indexes + CHECKs)"
```

---

## Phase 9 completion checklist

- [x] 21 FK constraints added covering every relational edge in the schema
- [x] `sessions.token_hash` unique index (seq-scan auth path fixed)
- [x] `sessions.expires_at` partial index (cleanup path fixed)
- [x] `channels.owner_id` partial index
- [x] `audit_logs.target_user_id` + `target_message_id` partial indexes
- [x] CHECK constraints on 4 enum-as-int columns (channel_invitations.status, friend_requests.status, channel_members.role, channels.kind)
- [x] All existing tests pass under FK enforcement
- [x] One consolidated migration `SchemaHardening`
- [x] No domain, contract, or endpoint changes required

## What was intentionally deferred

- **`varchar(n)` → `TEXT + CHECK(length(...))`** — stylistic per the postgres skill. Invasive (every column in the database), zero runtime benefit. Reassess if/when a text value needs to grow past its current `varchar` limit.
- **`attachments.message_id` NOT NULL** — by design (Phase 4): attachments start orphan and are bound by `SendMessage`; the orphan sweeper expects this shape.
- **Target FKs on audit_logs** — deliberately loose so audit records survive cascaded deletes of the target.
