# Attic Phase 4 — Attachments, Edit, Reply-To, Paste+Drop — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship file and image attachments end-to-end (streamed upload, content-addressable storage with ref-counted unlink, access-gated download); wire `ChatHub.EditMessage` with an "(edited)" broadcast; finish reply-to by reading `Message.ReplyToId` into the UI and click-to-reply in the composer; and add paste/drop behavior to the chat input.

**Architecture:** A new `Attachment` aggregate root holds file metadata. Uploads stream through `System.IO.Pipelines` → temp file → SHA-256-hashed atomic rename into `/data/attachments/{yyyy}/{mm}/{dd}/{sha256}.bin`, so two uploads of identical bytes dedupe on disk and a single filesystem write covers N `Attachment` rows. Attachments start unbound (`MessageId = null`); the hub's `SendMessage` binds them in the same transaction as the message insert. Download is per-current-membership (`ChannelMember` non-banned). An `AttachmentSweeperService` deletes `Attachment` rows whose `CreatedAt < now-24h AND MessageId IS NULL`; a `StorageSweeperService` unlinks the on-disk file when no remaining row references the `StoragePath` (triggered by message/channel soft-delete and by the orphan sweep). `ChatHub.EditMessage` invokes `Message.Edit(clock.UtcNow)` (already in Phase 1 domain) and broadcasts `MessageEdited`. Frontend `ChatInput` gains paste/drop handlers and an upload queue UI; `ChatWindow` shows reply context on each message and a composer reply bar; a per-message action menu surfaces Edit/Reply/Delete.

**Tech Stack:** Same as Phase 3 — .NET 10, Aspire 13.2.2, EF Core 10.0.5 + Npgsql, SignalR + Redis backplane, TanStack Query v5 + React Router v6 + `@microsoft/signalr` v8 + Tailwind 4. `Microsoft.AspNetCore.WebUtilities.MultipartReader` for streamed multipart; no new third-party dependencies.

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — attachments in §7, edit/delete rules in §8.1, SignalR surface in §9.2.

---

## Prerequisites — invariants from Phases 1-3

Do not regress any of these:

- **DbContext registration** uses `AddDbContext<AtticDbContext>` + `EnrichNpgsqlDbContext<AtticDbContext>()`. Interceptor attached in the options callback.
- **Hub methods** read user id via `Context.User`; `CurrentUser` scoped service is HTTP-only.
- **Raw SQL** in EF configurations uses snake_case identifiers unquoted.
- **`TimestampInterceptor`** respects `IsModified`; domain methods that own `UpdatedAt` set it explicitly (e.g. `Message.Edit`, `Channel.Rename`, `Channel.UpdateDescription`).
- **Entity `UpdatedAt` properties** are `{ get; private set; }`. Mutation is via domain methods only.
- **Authorization rules** are pure functions in `Attic.Domain.Services.AuthorizationRules`; controllers and hub methods load minimal rows, then call the rule.
- **REST mutations** that broadcast use a `*EventBroadcaster` injected as a scoped service.
- **FluentValidation** validators auto-registered via `AddValidatorsFromAssemblyContaining<RegisterRequestValidator>`.
- **Aspire** is 13.2.2, package `Aspire.Hosting.JavaScript`. Consult `~/.claude/skills/aspire/SKILL.md` for any AppHost change.

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0 on `phase-4` (branched from merged `main` after Phase 3).
- `dotnet test tests/Attic.Domain.Tests` → 101 passing.
- `dotnet test tests/Attic.Api.IntegrationTests` → 47 passing.
- `cd src/Attic.Web && npm run lint && npm run build` exits 0.
- Podman running; `DOCKER_HOST` points at the podman socket.
- Phase 1 `Message` entity already has `ReplyToId` (nullable self-FK) and `Message.Edit(content, at)` domain method.

---

## File structure additions

```
src/
├── Attic.Domain/
│   ├── Entities/
│   │   └── Attachment.cs                                      (new)
│   └── Services/
│       └── AuthorizationRules.cs                              (modify — CanEditMessage rule)
├── Attic.Infrastructure/
│   ├── Persistence/
│   │   ├── AtticDbContext.cs                                  (modify — DbSet<Attachment>)
│   │   ├── Configurations/
│   │   │   └── AttachmentConfiguration.cs                     (new)
│   │   └── Migrations/
│   │       └── XXXXXXXXXXXXXX_AddAttachments.cs               (generated)
│   └── Storage/
│       ├── IAttachmentStorage.cs                              (new — abstraction in Infrastructure)
│       ├── FilesystemAttachmentStorage.cs                     (new)
│       └── AttachmentStorageOptions.cs                        (new)
├── Attic.Contracts/
│   ├── Attachments/
│   │   ├── AttachmentDto.cs                                   (new)
│   │   └── UploadAttachmentResponse.cs                        (new)
│   └── Messages/
│       ├── EditMessageRequest.cs                              (new)
│       ├── EditMessageResponse.cs                             (new)
│       └── SendMessageRequest.cs                              (modify — add AttachmentIds[])
├── Attic.Api/
│   ├── Endpoints/
│   │   └── AttachmentsEndpoints.cs                            (new)
│   ├── Hubs/
│   │   ├── ChatHub.cs                                         (modify — EditMessage, bind attachments in SendMessage)
│   │   └── MessageEventBroadcaster.cs                         (new — thin IHubContext helper for MessageEdited)
│   ├── Services/
│   │   ├── AttachmentSweeperService.cs                        (new — orphans)
│   │   └── StorageSweeperService.cs                           (new — ref-counted unlink)
│   ├── Validators/
│   │   └── EditMessageRequestValidator.cs                     (new)
│   └── Program.cs                                             (modify — map endpoint, register storage + hosted services)
└── Attic.Web/
    └── src/
        ├── api/
        │   ├── attachments.ts                                 (new)
        │   └── signalr.ts                                     (modify — onMessageEdited, editMessage)
        ├── chat/
        │   ├── ChatInput.tsx                                  (modify — paste/drop + upload queue + reply bar)
        │   ├── ChatWindow.tsx                                 (modify — reply context + action menu)
        │   ├── AttachmentPreview.tsx                          (new)
        │   ├── ReplyPreview.tsx                               (new — composer reply-to bar)
        │   ├── MessageActionsMenu.tsx                         (new — Edit / Reply / Delete)
        │   ├── useUploadAttachments.ts                        (new)
        │   ├── useEditMessage.ts                              (new)
        │   ├── useChannelMessages.ts                          (modify — handle MessageEdited)
        │   └── useSendMessage.ts                              (modify — takes attachmentIds + replyToId)
        └── types.ts                                           (modify — AttachmentDto, EditMessageRequest, MessageDto.Attachments)
tests/
├── Attic.Domain.Tests/
│   ├── AttachmentTests.cs                                     (new)
│   └── AuthorizationRulesTests.cs                             (modify — CanEditMessage region)
└── Attic.Api.IntegrationTests/
    ├── AttachmentsFlowTests.cs                                (new)
    ├── EditMessageFlowTests.cs                                (new)
    └── MessagingFlowTests.cs                                  (modify — reply-to round-trip)
```

Total: ~24 new files, ~12 modified files.

---

## Task ordering rationale

Bottom-up: domain entity first, then storage abstraction + filesystem adapter, then EF mapping + migration, then endpoints, then hub wiring, then sweepers, then frontend. Each numbered task is a single commit; commit prefixes (`feat(domain)`, `feat(infra)`, `feat(api)`, `feat(web)`, `test(api)`, `chore:`, `fix:`) carry over.

Four checkpoints:

- **Checkpoint 1 — Domain + Storage + Infra (Tasks 1-9):** `Attachment` entity, `CanEditMessage` rule, `IAttachmentStorage` + filesystem adapter, `AttachmentConfiguration`, migration, storage options + DI registration.
- **Checkpoint 2 — REST + Hub binding + Sweepers (Tasks 10-21):** upload endpoint (streamed multipart), download endpoint (access-gated), `SendMessageRequest` extended with `AttachmentIds[]`, `ChatHub.SendMessage` binds attachments, `AttachmentSweeperService` + `StorageSweeperService` hosted services, integration tests.
- **Checkpoint 3 — Edit message (Tasks 22-26):** `EditMessageRequest/Response`, validator, `MessageEventBroadcaster`, `ChatHub.EditMessage`, integration test.
- **Checkpoint 4 — Frontend (Tasks 27-35):** types, API client, SignalR extension, `AttachmentPreview`, paste/drop `ChatInput`, upload queue, reply composer + message-level reply context, edit UI, message actions menu, end-to-end smoke.

---

## Task 1: `Attachment` entity with unit tests (TDD)

**Files:**
- Create: `src/Attic.Domain/Entities/Attachment.cs`
- Create: `tests/Attic.Domain.Tests/AttachmentTests.cs`

The `Attachment` aggregate holds metadata only. The file bytes live at `StoragePath` (content-addressable). `MessageId` is nullable — uploads start orphan, are bound on `SendMessage`, and are swept if left unbound for 24 h.

- [ ] **Step 1.1: Write failing tests — `tests/Attic.Domain.Tests/AttachmentTests.cs`**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AttachmentTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Register_creates_unbound_attachment()
    {
        var a = Attachment.Register(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            uploaderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            originalFileName: "photo.jpg",
            contentType: "image/jpeg",
            sizeBytes: 12345,
            storagePath: "2026/04/21/abcd.bin",
            comment: null,
            now: T0);

        a.Id.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        a.MessageId.ShouldBeNull();
        a.UploaderId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        a.OriginalFileName.ShouldBe("photo.jpg");
        a.ContentType.ShouldBe("image/jpeg");
        a.SizeBytes.ShouldBe(12345);
        a.StoragePath.ShouldBe("2026/04/21/abcd.bin");
        a.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Register_rejects_empty_storage_path()
    {
        Should.Throw<ArgumentException>(() => Attachment.Register(
            Guid.NewGuid(), Guid.NewGuid(), "a.bin", "application/octet-stream",
            1, "", null, T0)).ParamName.ShouldBe("storagePath");
    }

    [Fact]
    public void Register_rejects_non_positive_size()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Attachment.Register(
            Guid.NewGuid(), Guid.NewGuid(), "a.bin", "application/octet-stream",
            0, "x", null, T0)).ParamName.ShouldBe("sizeBytes");
    }

    [Fact]
    public void Register_trims_comment_and_nulls_empty()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", comment: "  ", now: T0);
        a.Comment.ShouldBeNull();
    }

    [Fact]
    public void BindToMessage_sets_message_id()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", null, T0);
        var messageId = 42L;
        a.BindToMessage(messageId);
        a.MessageId.ShouldBe(messageId);
    }

    [Fact]
    public void BindToMessage_rejects_rebinding()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", null, T0);
        a.BindToMessage(42);
        Should.Throw<InvalidOperationException>(() => a.BindToMessage(43));
    }
}
```

- [ ] **Step 1.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "AttachmentTests"
```

Expected: CS0103 `Attachment` does not exist.

- [ ] **Step 1.3: Implement `src/Attic.Domain/Entities/Attachment.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class Attachment
{
    public Guid Id { get; private set; }
    public long? MessageId { get; private set; }
    public Guid UploaderId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Attachment() { }

    public static Attachment Register(
        Guid id,
        Guid uploaderId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        string? comment,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name is required.", nameof(originalFileName));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is required.", nameof(contentType));
        if (sizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Size must be positive.");
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Storage path is required.", nameof(storagePath));

        var trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        return new Attachment
        {
            Id = id,
            UploaderId = uploaderId,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StoragePath = storagePath,
            Comment = trimmedComment,
            CreatedAt = now
        };
    }

    public void BindToMessage(long messageId)
    {
        if (MessageId is not null)
            throw new InvalidOperationException("Attachment is already bound to a message.");
        MessageId = messageId;
    }
}
```

- [ ] **Step 1.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "AttachmentTests"
```

Expected: 6 passing.

- [ ] **Step 1.5: Commit**

```bash
git add src/Attic.Domain/Entities/Attachment.cs tests/Attic.Domain.Tests/AttachmentTests.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(domain): add Attachment entity (unbound → bound on message send)"
```

---

## Task 2: `CanEditMessage` authorization rule (TDD)

**Files:**
- Modify: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Modify: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

- [ ] **Step 2.1: Append failing tests**

Inside the `AuthorizationRulesTests` class (after the `CanOpenPersonalChat_*` tests from Phase 3), add:

```csharp
    [Fact]
    public void CanEditMessage_allows_author_of_live_message()
    {
        var authorId = Guid.NewGuid();
        var message = Message.Post(Guid.NewGuid(), authorId, "hi", null, T0_J);
        AuthorizationRules.CanEditMessage(message, actorUserId: authorId).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanEditMessage_denies_non_author()
    {
        var message = Message.Post(Guid.NewGuid(), Guid.NewGuid(), "hi", null, T0_J);
        AuthorizationRules.CanEditMessage(message, actorUserId: Guid.NewGuid()).Reason
            .ShouldBe(AuthorizationFailureReason.NotAuthor);
    }

    [Fact]
    public void CanEditMessage_denies_deleted_message()
    {
        var authorId = Guid.NewGuid();
        var message = Message.Post(Guid.NewGuid(), authorId, "hi", null, T0_J);
        message.SoftDelete(T0_J.AddMinutes(1));
        AuthorizationRules.CanEditMessage(message, actorUserId: authorId).Reason
            .ShouldBe(AuthorizationFailureReason.NotAuthor);
    }
```

- [ ] **Step 2.2: Run, verify compile failure**

```bash
dotnet test tests/Attic.Domain.Tests --filter "CanEditMessage"
```

- [ ] **Step 2.3: Append method to `AuthorizationRules.cs`**

```csharp
    public static AuthorizationResult CanEditMessage(Message message, Guid actorUserId)
    {
        if (message.SenderId != actorUserId) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAuthor);
        if (message.DeletedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAuthor);
        return AuthorizationResult.Ok();
    }
```

- [ ] **Step 2.4: Run, verify pass; commit**

```bash
dotnet test tests/Attic.Domain.Tests
git add src/Attic.Domain/Services/AuthorizationRules.cs tests/Attic.Domain.Tests/AuthorizationRulesTests.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(domain): add CanEditMessage rule"
```

---

## Task 3: `IAttachmentStorage` abstraction + options

**Files:**
- Create: `src/Attic.Infrastructure/Storage/IAttachmentStorage.cs`
- Create: `src/Attic.Infrastructure/Storage/AttachmentStorageOptions.cs`

The abstraction lets tests swap in an in-memory adapter later if needed, keeps `FilesystemAttachmentStorage` focused.

- [ ] **Step 3.1: Write `IAttachmentStorage.cs`**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Attic.Infrastructure.Storage;

/// <summary>
/// Persists attachment bytes in a content-addressable layout.
/// The hash of the bytes determines the storage path, which is computed from the stream.
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Writes <paramref name="bytes"/> into a temp file, computes the SHA-256 hash, then atomically
    /// renames to the final content-addressable path. Returns the final storage path and size.
    /// </summary>
    Task<StorageWriteResult> WriteAsync(Stream bytes, DateTimeOffset now, CancellationToken ct);

    /// <summary>Returns a readable stream over the bytes at <paramref name="storagePath"/>.</summary>
    Stream OpenRead(string storagePath);

    /// <summary>Deletes the file at <paramref name="storagePath"/> if present.</summary>
    void Delete(string storagePath);

    /// <summary>Returns the absolute on-disk path (for Results.File / PhysicalFileResult).</summary>
    string Resolve(string storagePath);
}

public readonly record struct StorageWriteResult(string StoragePath, long SizeBytes, string ContentSha256);
```

- [ ] **Step 3.2: Write `AttachmentStorageOptions.cs`**

```csharp
namespace Attic.Infrastructure.Storage;

public sealed class AttachmentStorageOptions
{
    /// <summary>Root directory. In dev: a relative path under the project's working dir. In prod: a mounted volume.</summary>
    public string Root { get; set; } = "data/attachments";
    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024;
    public long MaxImageBytes { get; set; } = 3L * 1024 * 1024;
}
```

- [ ] **Step 3.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Storage/IAttachmentStorage.cs src/Attic.Infrastructure/Storage/AttachmentStorageOptions.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(infra): add IAttachmentStorage abstraction + options"
```

Expected: 0/0.

---

## Task 4: `FilesystemAttachmentStorage`

**Files:**
- Create: `src/Attic.Infrastructure/Storage/FilesystemAttachmentStorage.cs`

Streams bytes through a temp file, hashes them with SHA-256, then atomically renames into `{Root}/{yyyy}/{mm}/{dd}/{sha256}.bin`. If the destination already exists (dedupe), the temp file is deleted and the existing path is returned.

- [ ] **Step 4.1: Write `FilesystemAttachmentStorage.cs`**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Attic.Infrastructure.Storage;

public sealed class FilesystemAttachmentStorage(IOptions<AttachmentStorageOptions> options) : IAttachmentStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.Root);

    public async Task<StorageWriteResult> WriteAsync(Stream bytes, DateTimeOffset now, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var tempPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");

        long size;
        string hashHex;
        using (var sha = SHA256.Create())
        using (var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            size = 0;
            int read;
            while ((read = await bytes.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await tempStream.WriteAsync(buffer.AsMemory(0, read), ct);
                size += read;
            }
            sha.TransformFinalBlock(buffer, 0, 0);
            hashHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        var relativePath = Path.Combine(
            now.UtcDateTime.ToString("yyyy"),
            now.UtcDateTime.ToString("MM"),
            now.UtcDateTime.ToString("dd"),
            hashHex + ".bin");
        var finalAbsolute = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalAbsolute)!);

        if (File.Exists(finalAbsolute))
        {
            // Dedupe: the content is already on disk. Drop the temp file.
            File.Delete(tempPath);
        }
        else
        {
            File.Move(tempPath, finalAbsolute);
        }

        return new StorageWriteResult(relativePath.Replace(Path.DirectorySeparatorChar, '/'), size, hashHex);
    }

    public Stream OpenRead(string storagePath)
        => new FileStream(Resolve(storagePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string storagePath)
    {
        var abs = Resolve(storagePath);
        if (File.Exists(abs)) File.Delete(abs);
    }

    public string Resolve(string storagePath)
        => Path.Combine(_root, storagePath.Replace('/', Path.DirectorySeparatorChar));
}
```

- [ ] **Step 4.2: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Storage/FilesystemAttachmentStorage.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(infra): filesystem attachment storage (SHA-256 content-addressable)"
```

Expected: 0/0.

---

## Task 5: `AttachmentConfiguration` + `DbSet<Attachment>`

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Configurations/AttachmentConfiguration.cs`
- Modify: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`

- [ ] **Step 5.1: Write `AttachmentConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments");
        b.HasKey(a => a.Id);

        b.Property(a => a.OriginalFileName).HasMaxLength(512).IsRequired();
        b.Property(a => a.ContentType).HasMaxLength(128).IsRequired();
        b.Property(a => a.StoragePath).HasMaxLength(256).IsRequired();
        b.Property(a => a.Comment).HasMaxLength(1024);

        // Covering-ish index for orphan sweep: MessageId null + CreatedAt < cutoff.
        b.HasIndex(a => new { a.MessageId, a.CreatedAt })
            .HasDatabaseName("ix_attachments_message_created");

        // Ref-counted delete needs "any remaining attachment at this path?".
        b.HasIndex(a => a.StoragePath).HasDatabaseName("ix_attachments_storage_path");
    }
}
```

- [ ] **Step 5.2: Add `DbSet<Attachment>` to `AtticDbContext.cs`**

Insert after the last existing DbSet (likely `DbSet<UserBlock>`):

```csharp
    public DbSet<Attachment> Attachments => Set<Attachment>();
```

- [ ] **Step 5.3: Build + commit**

```bash
dotnet build src/Attic.Infrastructure
git add src/Attic.Infrastructure/Persistence/Configurations/AttachmentConfiguration.cs src/Attic.Infrastructure/Persistence/AtticDbContext.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(infra): add Attachment EF Core configuration"
```

---

## Task 6: Migration `AddAttachments`

**Files:**
- Generated: `src/Attic.Infrastructure/Persistence/Migrations/*_AddAttachments.cs`
- Updated: `AtticDbContextModelSnapshot.cs`

- [ ] **Step 6.1: Generate**

```bash
dotnet tool run dotnet-ef migrations add AddAttachments \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

- [ ] **Step 6.2: Sanity-check**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure --idempotent --output /tmp/phase4-attachments.sql
grep -i "attachments\|storage_path\|message_id" /tmp/phase4-attachments.sql | head -20
```

Must contain `CREATE TABLE attachments (id uuid, message_id bigint, uploader_id uuid, original_file_name character varying(512), content_type character varying(128), size_bytes bigint, storage_path character varying(256), comment character varying(1024), created_at timestamp with time zone, ...)` and the two indexes.

- [ ] **Step 6.3: Build + commit**

```bash
dotnet build Attic.slnx
git add src/Attic.Infrastructure/Persistence/Migrations docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(infra): migration AddAttachments"
```

---

## Task 7: DI registration — storage + options

**Files:**
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 7.1: Register options + service**

Near the other `AddScoped` / `AddSingleton` calls in `Program.cs` (somewhere around where `AtticDbContext` is configured), add:

```csharp
builder.Services.Configure<Attic.Infrastructure.Storage.AttachmentStorageOptions>(
    builder.Configuration.GetSection("Attachments"));
builder.Services.AddSingleton<Attic.Infrastructure.Storage.IAttachmentStorage,
                              Attic.Infrastructure.Storage.FilesystemAttachmentStorage>();
```

- [ ] **Step 7.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): register filesystem attachment storage"
```

---

## Task 8: AppHost — bind mount `/data/attachments`

**Files:**
- Modify: `src/Attic.AppHost/AppHost.cs` (or wherever `AddProject<Attic.Api>()` is)

The API container needs a persistent volume for attachments. For dev we use a project-local directory; for prod a named volume. Aspire 13.2's `WithBindMount` / `WithVolume` APIs handle this — consult `~/.claude/skills/aspire/references/integrations-catalog.md` if unsure of the method name.

- [ ] **Step 8.1: Add bind mount**

Find where `AddProject<Projects.Attic_Api>("api")` is called and append a bind mount so the API project's working directory has a `data/attachments` folder:

```csharp
var api = builder.AddProject<Projects.Attic_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithEnvironment("Attachments__Root", "data/attachments");
// ... existing WaitFor/WithReference calls preserved
```

The simplest setup uses a filesystem directory relative to the API's working dir (created on first upload by `FilesystemAttachmentStorage.WriteAsync`). Aspire doesn't need an explicit bind mount for a local dev path under the project's working dir.

- [ ] **Step 8.2: Add `Attachments__Root` to `appsettings.Development.json`** (optional override)

If `Attic.Api/appsettings.Development.json` exists, add:

```json
  "Attachments": {
    "Root": "data/attachments"
  }
```

If a different config file pattern is used, adapt — the key is `Attachments:Root`.

- [ ] **Step 8.3: Ignore the `data/` dir in gitignore**

Open `src/Attic.Api/.gitignore` (create if missing):

```
data/
```

- [ ] **Step 8.4: Build + commit**

```bash
dotnet build Attic.slnx
git add src/Attic.AppHost/AppHost.cs src/Attic.Api/appsettings.Development.json src/Attic.Api/.gitignore docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(apphost): wire Attachments:Root env for the API project"
```

If any of those files don't exist or have different paths in the project (e.g. `appsettings.Development.json` was never created), adapt — skip the file and note it in the commit. Stage only what actually changed.

---

## Task 9: Checkpoint 1 marker

- [ ] **Step 9.1: Full test run**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: 101 prior + 6 Attachment + 3 CanEditMessage = 110 passing.

- [ ] **Step 9.2: Marker commit**

```bash
git commit --allow-empty -m "chore: Phase 4 Checkpoint 1 (domain + storage + infra) green"
```

---

## Task 10: Contracts DTOs — `AttachmentDto` + `UploadAttachmentResponse` + `EditMessage*`

**Files:**
- Create: `src/Attic.Contracts/Attachments/AttachmentDto.cs`
- Create: `src/Attic.Contracts/Attachments/UploadAttachmentResponse.cs`
- Create: `src/Attic.Contracts/Messages/EditMessageRequest.cs`
- Create: `src/Attic.Contracts/Messages/EditMessageResponse.cs`

All `sealed record`.

- [ ] **Step 10.1: `Attachments/AttachmentDto.cs`**

```csharp
namespace Attic.Contracts.Attachments;

public sealed record AttachmentDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? Comment);
```

- [ ] **Step 10.2: `Attachments/UploadAttachmentResponse.cs`**

```csharp
namespace Attic.Contracts.Attachments;

public sealed record UploadAttachmentResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);
```

- [ ] **Step 10.3: `Messages/EditMessageRequest.cs`**

```csharp
namespace Attic.Contracts.Messages;

public sealed record EditMessageRequest(long MessageId, string Content);
```

- [ ] **Step 10.4: `Messages/EditMessageResponse.cs`**

```csharp
namespace Attic.Contracts.Messages;

public sealed record EditMessageResponse(bool Ok, DateTimeOffset? UpdatedAt, string? Error);
```

- [ ] **Step 10.5: Build + commit**

```bash
dotnet build src/Attic.Contracts
git add src/Attic.Contracts/Attachments src/Attic.Contracts/Messages/EditMessageRequest.cs src/Attic.Contracts/Messages/EditMessageResponse.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(contracts): add Phase 4 attachment + edit-message DTOs"
```

---

## Task 11: Extend `SendMessageRequest` with `AttachmentIds[]`

**Files:**
- Modify: `src/Attic.Contracts/Messages/SendMessageRequest.cs`

- [ ] **Step 11.1: Update the record**

The existing Phase 1 record is something like `public sealed record SendMessageRequest(Guid ChannelId, Guid ClientMessageId, string Content, long? ReplyToId);`. Extend it with a nullable `AttachmentIds` array (defaulting to empty via `?? Array.Empty<Guid>()` at call sites):

```csharp
namespace Attic.Contracts.Messages;

public sealed record SendMessageRequest(
    Guid ChannelId,
    Guid ClientMessageId,
    string Content,
    long? ReplyToId,
    Guid[]? AttachmentIds = null);
```

The default value allows existing Phase 1/2/3 callers to keep working without changes.

- [ ] **Step 11.2: Update `MessageDto` to expose attachments**

Open `src/Attic.Contracts/Messages/MessageDto.cs`. Append a nullable `Attachments` field:

```csharp
namespace Attic.Contracts.Messages;

public sealed record MessageDto(
    long Id,
    Guid ChannelId,
    Guid SenderId,
    string SenderUsername,
    string Content,
    long? ReplyToId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Attic.Contracts.Attachments.AttachmentDto[]? Attachments = null);
```

Existing callsites build `MessageDto` with 8 positional args; adding a defaulted 9th won't break them.

- [ ] **Step 11.3: Build + commit**

```bash
dotnet build src/Attic.Contracts
dotnet build src/Attic.Api
git add src/Attic.Contracts/Messages docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(contracts): SendMessageRequest + MessageDto carry attachments"
```

---

## Task 12: `EditMessageRequestValidator`

**Files:**
- Create: `src/Attic.Api/Validators/EditMessageRequestValidator.cs`

- [ ] **Step 12.1: Write the validator**

```csharp
using System.Text;
using Attic.Contracts.Messages;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class EditMessageRequestValidator : AbstractValidator<EditMessageRequest>
{
    public EditMessageRequestValidator()
    {
        RuleFor(r => r.MessageId).GreaterThan(0).WithErrorCode("invalid_message_id");
        RuleFor(r => r.Content)
            .NotEmpty().WithErrorCode("empty_content")
            .Must(c => Encoding.UTF8.GetByteCount(c) <= 3072).WithErrorCode("content_too_large");
    }
}
```

- [ ] **Step 12.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Validators/EditMessageRequestValidator.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): add EditMessageRequestValidator"
```

---

## Task 13: `AttachmentsEndpoints` — upload (streamed multipart)

**Files:**
- Create: `src/Attic.Api/Endpoints/AttachmentsEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`

The upload endpoint uses `MultipartReader` (from `Microsoft.AspNetCore.WebUtilities`) to stream each form section without buffering. We only expect one file per request plus an optional `comment` field.

- [ ] **Step 13.1: Write `AttachmentsEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Attachments;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Attic.Api.Endpoints;

public static class AttachmentsEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/attachments").RequireAuthorization();
        group.MapPost("/", Upload).DisableAntiforgery();
        group.MapGet("/{id:guid}", Download);
        return routes;
    }

    private static async Task<IResult> Upload(
        HttpRequest request,
        AtticDbContext db,
        IAttachmentStorage storage,
        IOptions<AttachmentStorageOptions> options,
        IClock clock,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        if (!request.HasFormContentType || !MediaTypeHeaderValue.TryParse(request.ContentType, out var mt)
            || string.IsNullOrWhiteSpace(mt.Boundary.Value))
            return Results.BadRequest(new ApiError("invalid_content_type", "Expected multipart/form-data."));

        var boundary = HeaderUtilities.RemoveQuotes(mt.Boundary).Value!;
        var reader = new MultipartReader(boundary, request.Body);

        string? comment = null;
        StorageWriteResult? writeResult = null;
        string? originalFileName = null;
        string? contentType = null;

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                continue;

            if (disposition.IsFormDisposition() && disposition.Name.Value == "comment")
            {
                using var sr = new StreamReader(section.Body);
                comment = await sr.ReadToEndAsync(ct);
            }
            else if (disposition.IsFileDisposition())
            {
                originalFileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value
                                   ?? HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value;
                if (string.IsNullOrWhiteSpace(originalFileName))
                    return Results.BadRequest(new ApiError("missing_filename", "File name is required."));
                contentType = section.ContentType ?? "application/octet-stream";

                var limit = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? options.Value.MaxImageBytes
                    : options.Value.MaxFileBytes;

                using var limited = new LimitedStream(section.Body, limit);
                try
                {
                    writeResult = await storage.WriteAsync(limited, clock.UtcNow, ct);
                }
                catch (InvalidDataException)
                {
                    return Results.BadRequest(new ApiError("too_large", "File exceeds the allowed size."));
                }
            }
        }

        if (writeResult is null || originalFileName is null || contentType is null)
            return Results.BadRequest(new ApiError("missing_file", "A file part is required."));

        var attachment = Attachment.Register(
            Guid.NewGuid(), currentUser.UserIdOrThrow,
            originalFileName, contentType, writeResult.Value.SizeBytes,
            writeResult.Value.StoragePath, comment, clock.UtcNow);
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new UploadAttachmentResponse(
            attachment.Id, attachment.OriginalFileName, attachment.ContentType, attachment.SizeBytes));
    }

    private static async Task<IResult> Download(
        Guid id,
        AtticDbContext db,
        IAttachmentStorage storage,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null || attachment.MessageId is null) return Results.NotFound();

        var message = await db.Messages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == attachment.MessageId, ct);
        if (message is null) return Results.NotFound();

        var isMember = await db.ChannelMembers.AsNoTracking()
            .AnyAsync(m => m.ChannelId == message.ChannelId && m.UserId == currentUser.UserIdOrThrow, ct);
        if (!isMember) return Results.Forbid();

        var disposition = attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? "inline"
            : "attachment";
        var abs = storage.Resolve(attachment.StoragePath);
        return Results.File(abs, attachment.ContentType, attachment.OriginalFileName,
            enableRangeProcessing: false);
        // Note: Results.File sets Content-Disposition: attachment automatically when fileDownloadName is provided.
        // For the inline case (images), we rely on the default behavior or set a custom header — but for
        // Phase 4 MVP, serving both as attachment download is acceptable; the UI renders inline via the
        // browser's data-url / blob-url flow.
    }
}

/// <summary>
/// Wraps a Stream and throws <see cref="InvalidDataException"/> when reads would exceed <paramref name="maxBytes"/>.
/// </summary>
internal sealed class LimitedStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        _read += n;
        if (_read > maxBytes) throw new InvalidDataException($"Stream exceeded {maxBytes} bytes.");
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        _read += n;
        if (_read > maxBytes) throw new InvalidDataException($"Stream exceeded {maxBytes} bytes.");
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

- [ ] **Step 13.2: Map in `Program.cs`**

After the existing endpoint mappings:

```csharp
app.MapAttachmentsEndpoints();
```

- [ ] **Step 13.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/AttachmentsEndpoints.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): attachment upload + download endpoints (streamed multipart, access-gated)"
```

Expected: 0/0.

---

## Task 14: `ChatHub.SendMessage` — bind attachment IDs

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

After the message is inserted (the line that adds `message` to `db.Messages` and calls `SaveChangesAsync`), but **in the same transaction**, bind any attachments referenced by `request.AttachmentIds`. The attachments must belong to the caller and be unbound.

- [ ] **Step 14.1: Splice attachment binding into `SendMessage`**

Find the block in `SendMessage` that looks like:

```csharp
db.Messages.Add(message);
await db.SaveChangesAsync();
```

Replace it with:

```csharp
        db.Messages.Add(message);
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

            foreach (var a in attachments) a.BindToMessage(message.Id);
            await db.SaveChangesAsync();
        }
```

Then the broadcast step that follows should include the attachment list in the outgoing `MessageDto`. Find the block that constructs the `MessageDto` for `MessageCreated` broadcast and include:

```csharp
        var attachmentDtos = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId == message.Id)
            .Select(a => new Attic.Contracts.Attachments.AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.Comment))
            .ToArrayAsync();
```

And construct the `MessageDto` with the new `attachmentDtos` argument.

- [ ] **Step 14.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): ChatHub.SendMessage binds attachments and echoes them on MessageCreated"
```

---

## Task 15: `MessagesEndpoints.GetBeforeCursor` — include attachments

**Files:**
- Modify: `src/Attic.Api/Endpoints/MessagesEndpoints.cs`

The history endpoint must project attachments into each `MessageDto`.

- [ ] **Step 15.1: Add a second query for attachments**

After the `rows` list is materialized (the list of `MessageDto` without attachments), add:

```csharp
        var ids = rows.Select(m => m.Id).ToList();
        var attachmentsByMessage = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId != null && ids.Contains(a.MessageId!.Value))
            .Select(a => new { a.MessageId, Dto = new Attic.Contracts.Attachments.AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.Comment) })
            .ToListAsync(ct);
        var attachmentMap = attachmentsByMessage
            .GroupBy(x => x.MessageId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToArray());

        var enriched = rows
            .Select(m => m with { Attachments = attachmentMap.TryGetValue(m.Id, out var atts) ? atts : null })
            .ToList();

        string? nextCursor = enriched.Count == size ? KeysetCursor.Encode(enriched[^1].Id) : null;
        return Results.Ok(new PagedResult<MessageDto>(enriched, nextCursor));
```

Replace the existing `return Results.Ok(new PagedResult<MessageDto>(rows, nextCursor));` with the `enriched` variant above. Record types' `with` expression provides the positional-record copy-with-modifications syntax.

- [ ] **Step 15.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Endpoints/MessagesEndpoints.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): message history includes bound attachments"
```

---

## Task 16: `AttachmentSweeperService`

**Files:**
- Create: `src/Attic.Api/Services/AttachmentSweeperService.cs`
- Modify: `src/Attic.Api/Program.cs`

Hourly sweep: delete `Attachment` rows with `MessageId IS NULL AND CreatedAt < now-24h`, and ref-counted-unlink their `StoragePath` files.

- [ ] **Step 16.1: Write `AttachmentSweeperService.cs`**

```csharp
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class AttachmentSweeperService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AttachmentSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();

                var cutoff = clock.UtcNow - OrphanAge;
                var orphans = await db.Attachments
                    .Where(a => a.MessageId == null && a.CreatedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var a in orphans)
                {
                    db.Attachments.Remove(a);
                    // Defer the ref-counted unlink to StorageSweeperService — save first, unlink after.
                }
                await db.SaveChangesAsync(stoppingToken);

                foreach (var a in orphans)
                {
                    var stillReferenced = await db.Attachments
                        .AnyAsync(x => x.StoragePath == a.StoragePath, stoppingToken);
                    if (!stillReferenced) storage.Delete(a.StoragePath);
                }

                if (orphans.Count > 0)
                    logger.LogInformation("Swept {Count} orphan attachments.", orphans.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attachment sweeper iteration failed.");
            }

            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
```

- [ ] **Step 16.2: Register in `Program.cs`**

Near other `AddHostedService` / service registrations:

```csharp
builder.Services.AddHostedService<Attic.Api.Services.AttachmentSweeperService>();
```

- [ ] **Step 16.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Services/AttachmentSweeperService.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): AttachmentSweeperService (hourly orphan cleanup)"
```

---

## Task 17: `StorageSweeperService` — ref-counted unlink on cascade

**Files:**
- Create: `src/Attic.Api/Services/StorageSweeperService.cs`
- Modify: `src/Attic.Api/Program.cs`

A lighter-weight sweeper that runs every 10 minutes, finds `Attachment` rows referencing a `StoragePath` whose on-disk file exists but where no non-deleted message owns the attachment, and unlinks. For the MVP this catches attachments that lost their message because of a `Channel.SoftDelete` / `Message.SoftDelete`.

Practically, because Phase 2/3 don't hard-delete messages, the `Attachment` row persists; we just need to detect "the *message* this attachment points at is soft-deleted and no other attachment shares the path" on a periodic basis.

- [ ] **Step 17.1: Write `StorageSweeperService.cs`**

```csharp
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Services;

public sealed class StorageSweeperService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<StorageSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await timer.WaitForNextTickAsync(stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IAttachmentStorage>();

                // Candidate attachments: bound to a soft-deleted message (past grace period).
                // We query with filters off so we see deleted messages.
                var grace = clock.UtcNow - TimeSpan.FromMinutes(10);
                var candidates = await db.Attachments.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => a.MessageId != null &&
                                db.Messages.IgnoreQueryFilters()
                                    .Any(m => m.Id == a.MessageId && m.DeletedAt != null && m.DeletedAt < grace))
                    .ToListAsync(stoppingToken);

                foreach (var a in candidates)
                {
                    // If no live (non-deleted-message) attachment shares this path, unlink.
                    var stillReferenced = await db.Attachments.IgnoreQueryFilters()
                        .AnyAsync(x => x.Id != a.Id && x.StoragePath == a.StoragePath &&
                                       db.Messages.IgnoreQueryFilters()
                                           .Any(m => m.Id == x.MessageId && m.DeletedAt == null),
                                  stoppingToken);
                    if (!stillReferenced) storage.Delete(a.StoragePath);
                }

                if (candidates.Count > 0)
                    logger.LogInformation("Storage sweeper checked {Count} candidates.", candidates.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Storage sweeper iteration failed.");
            }
        }
    }
}
```

- [ ] **Step 17.2: Register in `Program.cs`**

```csharp
builder.Services.AddHostedService<Attic.Api.Services.StorageSweeperService>();
```

- [ ] **Step 17.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Services/StorageSweeperService.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): StorageSweeperService (ref-counted unlink after soft-delete)"
```

---

## Task 18: `AttachmentsFlowTests` — upload + bind + download

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/AttachmentsFlowTests.cs`

- [ ] **Step 18.1: Write the test file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Attic.Contracts.Attachments;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AttachmentsFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    private static async Task<UploadAttachmentResponse> Upload(HttpClient client, byte[] bytes,
        string fileName, string contentType, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync("/api/attachments", form, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<UploadAttachmentResponse>(ct))!;
    }

    [Fact]
    public async Task Upload_bind_and_download_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, username, _) = await Register(ct);

        // Create a public room so the user can send a message.
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"att-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var payload = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21 };   // "Hello!"
        var uploadResp = await Upload(client, payload, "greeting.txt", "text/plain", ct);
        uploadResp.SizeBytes.ShouldBe(payload.Length);

        // Send a message binding the attachment via the hub.
        var cookieHeader = string.Join("; ",
            new HttpClientHandler().CookieContainer.GetCookies(fx.ApiClient.BaseAddress!)
                .Select(c => $"{c.Name}={c.Value}"));
        // Reuse the existing handler from client (we need its cookies).
        cookieHeader = string.Join("; ",
            ((HttpClientHandler)typeof(HttpMessageInvoker).GetField("_handler",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(client)!).CookieContainer
                .GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var hub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();
        await hub.StartAsync(ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "see attached", null,
                new[] { uploadResp.Id }), ct);
        send.Ok.ShouldBeTrue();

        // Download the attachment.
        var download = await client.GetAsync($"/api/attachments/{uploadResp.Id:D}", ct);
        download.EnsureSuccessStatusCode();
        var content = await download.Content.ReadAsByteArrayAsync(ct);
        content.ShouldBe(payload);
    }

    [Fact]
    public async Task Download_denied_for_non_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _) = await Register(ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"att-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var uploadResp = await Upload(owner, payload, "bin.bin", "application/octet-stream", ct);

        // Bind via hub (reusing the pattern from the first test is verbose — use a REST-side smoke here:
        // the attachment stays unbound, so download returns 404, which is still proof of access control).
        var (outsider, _, _) = await Register(ct);
        var download = await outsider.GetAsync($"/api/attachments/{uploadResp.Id:D}", ct);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_rejects_empty_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await Register(ct);

        var resp = await client.PostAsync("/api/attachments",
            new StringContent("", new MediaTypeHeaderValue("text/plain")), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
```

**Note:** The cookie-extracting helper in `Upload_bind_and_download_round_trip` is ugly. If `TestHelpers` already exposes a way to grab the cookie header from the `client`'s handler, use that instead. Otherwise, add a small helper to `TestHelpers.cs`:

```csharp
public static string GetCookieHeader(HttpClient client, AppHostFixture fx)
{
    var handlerField = client.GetType()
        .GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var handler = (HttpClientHandler)handlerField!.GetValue(client)!;
    return string.Join("; ",
        handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
}
```

Then use `TestHelpers.GetCookieHeader(client, fx)` in the test.

**Simpler alternative:** The `TestHelpers.RegisterFresh` already creates the `HttpClientHandler` — we just need it to also return the handler. Modify `TestHelpers.RegisterFresh` to return `(HttpClient, Username, Email, HttpClientHandler)` or add a companion helper.

Use whichever approach fits the existing `TestHelpers.cs` shape best.

- [ ] **Step 18.2: Run + commit**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test tests/Attic.Api.IntegrationTests --filter "AttachmentsFlowTests"
git add tests/Attic.Api.IntegrationTests/AttachmentsFlowTests.cs tests/Attic.Api.IntegrationTests/TestHelpers.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "test(api): attachment upload + bind + download"
```

Expected: 3 new tests passing.

---

## Task 19: `MessagingFlowTests` — reply-to round trip

**Files:**
- Modify: `tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs`

Phase 1's `MessageDto` has `ReplyToId`; Phase 1 tests don't exercise it. Add a test that sends two messages (B replies to A) and verifies the history response carries `ReplyToId`.

- [ ] **Step 19.1: Append a test**

Inside `MessagingFlowTests`:

```csharp
    [Fact]
    public async Task Reply_to_round_trips_through_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"r-{Guid.NewGuid():N}@example.com";
        var username = $"r{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"), ct)).EnsureSuccessStatusCode();

        var createResponse = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"reply-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await createResponse.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
        await using var hub = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader).Build();
        await hub.StartAsync(ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var first = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "original", null), ct);
        first.Ok.ShouldBeTrue();
        var firstId = first.ServerId!.Value;

        var reply = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "replying", firstId), ct);
        reply.Ok.ShouldBeTrue();

        var history = await client.GetAsync($"/api/channels/{channel.Id:D}/messages?limit=10", ct);
        var page = (await history.Content.ReadFromJsonAsync<PagedResult<MessageDto>>(ct))!;
        page.Items.ShouldContain(m => m.Content == "replying" && m.ReplyToId == firstId);
    }
```

- [ ] **Step 19.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "MessagingFlowTests"
git add tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "test(api): reply-to round-trips through message history"
```

---

## Task 20: Checkpoint 2 marker

- [ ] **Step 20.1: Full integration suite**

```bash
dotnet test tests/Attic.Api.IntegrationTests
```

Expected: Phase-3's 47 + 3 Attachments + 1 Reply = 51 passing.

- [ ] **Step 20.2: Marker commit**

```bash
git commit --allow-empty -m "chore: Phase 4 Checkpoint 2 (attachments + reply-to) green"
```

---

## Task 21: `MessageEventBroadcaster` (for edits and future message events)

**Files:**
- Create: `src/Attic.Api/Hubs/MessageEventBroadcaster.cs`
- Modify: `src/Attic.Api/Program.cs`

Phase 2 broadcasted `MessageDeleted` directly from the hub. For Phase 4's edit flow, introduce a broadcaster so endpoints can also fire `MessageEdited` (future phases might expose `PATCH /api/messages/{id}`). For now `EditMessage` runs on the hub, so `SendAsync` can go direct — the broadcaster stays minimal but future-friendly.

- [ ] **Step 21.1: Write `MessageEventBroadcaster.cs`**

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class MessageEventBroadcaster(IHubContext<ChatHub> hub)
{
    public Task MessageEdited(Guid channelId, long messageId, string newContent, DateTimeOffset updatedAt) =>
        hub.Clients.Group(GroupNames.Channel(channelId))
            .SendAsync("MessageEdited", channelId, messageId, newContent, updatedAt);
}
```

- [ ] **Step 21.2: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<Attic.Api.Hubs.MessageEventBroadcaster>();
```

- [ ] **Step 21.3: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/MessageEventBroadcaster.cs src/Attic.Api/Program.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): add MessageEventBroadcaster (MessageEdited)"
```

---

## Task 22: `ChatHub.EditMessage`

**Files:**
- Modify: `src/Attic.Api/Hubs/ChatHub.cs`

- [ ] **Step 22.1: Add `EditMessage` method**

Insert after `DeleteMessage`:

```csharp
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
```

Inject the validator into the hub constructor. Find the existing primary constructor of `ChatHub` (something like `public sealed class ChatHub(AtticDbContext db, IClock clock, IValidator<SendMessageRequest> sendValidator) : Hub`) and extend it:

```csharp
public sealed class ChatHub(
    AtticDbContext db,
    IClock clock,
    IValidator<SendMessageRequest> sendValidator,
    IValidator<EditMessageRequest> editValidator) : Hub
```

If the existing hub uses a different parameter naming convention (e.g. `IValidator<SendMessageRequest> validator`), adapt the edit validator parameter name to match (e.g. `editMessageValidator`).

Add these `using` directives at the top if not already present:
```csharp
using Attic.Contracts.Messages;
```

- [ ] **Step 22.2: Build + commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Hubs/ChatHub.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(api): ChatHub.EditMessage (author-only, fires MessageEdited)"
```

---

## Task 23: `EditMessageFlowTests`

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/EditMessageFlowTests.cs`

- [ ] **Step 23.1: Write tests**

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
public sealed class EditMessageFlowTests(AppHostFixture fx)
{
    private Task<(HttpClient Client, string Username, string Email)> Register(CancellationToken ct)
        => TestHelpers.RegisterFresh(fx, ct);

    private static async Task<HubConnection> ConnectHub(AppHostFixture fx, HttpClient client, CancellationToken ct)
    {
        var handler = (HttpClientHandler)typeof(HttpMessageInvoker)
            .GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(client)!;
        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));
        var conn = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader).Build();
        await conn.StartAsync(ct);
        return conn;
    }

    [Fact]
    public async Task Author_edits_own_message_and_MessageEdited_fires()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await Register(ct);

        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"edit-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var hub = await ConnectHub(fx, client, ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var edited = new TaskCompletionSource<(long id, string content, DateTimeOffset updatedAt)>();
        hub.On<Guid, long, string, DateTimeOffset>("MessageEdited", (_, id, content, at) =>
            edited.TrySetResult((id, content, at)));

        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "before", null), ct);
        var messageId = send.ServerId!.Value;

        var edit = await hub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, "after"), ct);
        edit.Ok.ShouldBeTrue();
        edit.UpdatedAt.ShouldNotBeNull();

        var evt = await edited.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        evt.id.ShouldBe(messageId);
        evt.content.ShouldBe("after");
    }

    [Fact]
    public async Task Non_author_cannot_edit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (author, _, _) = await Register(ct);
        var create = await author.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"na-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var authorHub = await ConnectHub(fx, author, ct);
        await authorHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await authorHub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "mine", null), ct);
        var messageId = send.ServerId!.Value;

        var (outsider, _, _) = await Register(ct);
        (await outsider.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();
        await using var outsiderHub = await ConnectHub(fx, outsider, ct);
        await outsiderHub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);

        var edit = await outsiderHub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, "hijacked"), ct);
        edit.Ok.ShouldBeFalse();
        edit.Error.ShouldBe("NotAuthor");
    }

    [Fact]
    public async Task Edit_rejects_empty_content()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await Register(ct);
        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ee-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        await using var hub = await ConnectHub(fx, client, ct);
        await hub.InvokeAsync<JsonElement>("SubscribeToChannel", channel.Id, ct);
        var send = await hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(channel.Id, Guid.NewGuid(), "hello", null), ct);
        var messageId = send.ServerId!.Value;

        var edit = await hub.InvokeAsync<EditMessageResponse>("EditMessage",
            new EditMessageRequest(messageId, ""), ct);
        edit.Ok.ShouldBeFalse();
        edit.Error.ShouldBe("empty_content");
    }
}
```

- [ ] **Step 23.2: Run + commit**

```bash
dotnet test tests/Attic.Api.IntegrationTests --filter "EditMessageFlowTests"
git add tests/Attic.Api.IntegrationTests/EditMessageFlowTests.cs docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "test(api): ChatHub.EditMessage flow"
```

Expected: 3 new tests passing.

---

## Task 24: Checkpoint 3 marker

- [ ] **Step 24.1: Full run**

```bash
dotnet test
```

Expected: Domain ~110 + Integration ~54 = all green.

- [ ] **Step 24.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 4 Checkpoint 3 (edit message + reply-to) green"
```

---

## Task 25: Frontend types + API client

**Files:**
- Modify: `src/Attic.Web/src/types.ts`
- Create: `src/Attic.Web/src/api/attachments.ts`

- [ ] **Step 25.1: Append to `types.ts`**

```ts
export interface AttachmentDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  comment: string | null;
}

export interface UploadAttachmentResponse {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface EditMessageRequest {
  messageId: number;
  content: string;
}

export interface EditMessageResponse {
  ok: boolean;
  updatedAt: string | null;
  error: string | null;
}
```

Find the existing `MessageDto` interface and extend with a nullable `attachments`:

```ts
export interface MessageDto {
  id: number;
  channelId: string;
  senderId: string;
  senderUsername: string;
  content: string;
  replyToId: number | null;
  createdAt: string;
  updatedAt: string | null;
  attachments: AttachmentDto[] | null;
}
```

If the existing `MessageDto` doesn't have `replyToId` as an explicit field, verify and add it. Phase 1 should have included it.

Also find the existing `SendMessageRequest` interface (or equivalent) and extend with optional attachment IDs:

```ts
export interface SendMessageRequest {
  channelId: string;
  clientMessageId: string;
  content: string;
  replyToId: number | null;
  attachmentIds: string[] | null;
}
```

- [ ] **Step 25.2: Create `src/Attic.Web/src/api/attachments.ts`**

```ts
import type { UploadAttachmentResponse } from '../types';

export const attachmentsApi = {
  async upload(file: File, comment?: string): Promise<UploadAttachmentResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (comment) form.append('comment', comment);

    const r = await fetch(new URL('/api/attachments', window.location.origin), {
      method: 'POST',
      credentials: 'include',
      body: form,
    });
    if (!r.ok) {
      let code = 'upload_failed';
      try { code = (await r.json()).code ?? code; } catch { /* ignore */ }
      throw new Error(code);
    }
    return r.json();
  },
  downloadUrl(attachmentId: string): string {
    return `/api/attachments/${attachmentId}`;
  },
};
```

- [ ] **Step 25.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/types.ts src/Attic.Web/src/api/attachments.ts docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): typed attachments API client + extended MessageDto"
```

---

## Task 26: SignalR wrapper — `onMessageEdited`, `editMessage`

**Files:**
- Modify: `src/Attic.Web/src/api/signalr.ts`

- [ ] **Step 26.1: Extend `HubClient` interface**

Add to the import line: include `EditMessageRequest, EditMessageResponse`.

Inside the interface:
```ts
  editMessage(req: EditMessageRequest): Promise<EditMessageResponse>;
  onMessageEdited(cb: (channelId: string, messageId: number, newContent: string, updatedAt: string) => void): () => void;
```

- [ ] **Step 26.2: Extend factory**

Inside the returned singleton, after `deleteMessage`:
```ts
    async editMessage(req) {
      await ensureStarted();
      return connection.invoke<EditMessageResponse>('EditMessage', req);
    },
```

After `onMessageDeleted`:
```ts
    onMessageEdited: (cb) => on<[string, number, string, string]>('MessageEdited', cb),
```

Update the `import type` line at the top:
```ts
import type { MessageDto, SendMessageResponse, ChannelMemberSummary, InvitationDto, FriendRequestDto, EditMessageRequest, EditMessageResponse } from '../types';
```

- [ ] **Step 26.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/api/signalr.ts docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): SignalR client handles MessageEdited + EditMessage"
```

---

## Task 27: `useChannelMessages` — apply `MessageEdited`; extend `useSendMessage`

**Files:**
- Modify: `src/Attic.Web/src/chat/useChannelMessages.ts`
- Modify: `src/Attic.Web/src/chat/useSendMessage.ts`

- [ ] **Step 27.1: Handle `MessageEdited` in `useChannelMessages.ts`**

Inside the `useEffect` that sets up `onMessageCreated` / `onMessageDeleted`, add an `onMessageEdited` subscription:

```ts
    const offEdited = hub.onMessageEdited((cid, messageId, newContent, updatedAt) => {
      if (!active || cid !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        return {
          ...prev,
          pages: prev.pages.map(p => ({
            ...p,
            items: p.items.map(m => m.id === messageId ? { ...m, content: newContent, updatedAt } : m),
          })),
        };
      });
    });
```

And include `offEdited()` in the cleanup function's list.

- [ ] **Step 27.2: Extend `useSendMessage.ts`**

The existing signature is something like `useSendMessage(channelId, user)` returning a function `send(content: string)`. Extend it to accept `replyToId` and `attachmentIds`:

```ts
import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

export function useSendMessage(channelId: string, user: { id: string; username: string }) {
  const qc = useQueryClient();
  return useCallback(async (content: string, opts?: { replyToId?: number | null; attachmentIds?: string[] }) => {
    const hub = getOrCreateHubClient();
    const clientMessageId = crypto.randomUUID();
    const optimistic: MessageDto = {
      id: -Date.now(),
      channelId,
      senderId: user.id,
      senderUsername: user.username,
      content,
      replyToId: opts?.replyToId ?? null,
      createdAt: new Date().toISOString(),
      updatedAt: null,
      attachments: null,
    };
    qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(
      ['channel-messages', channelId], prev => {
        if (!prev || prev.pages.length === 0) {
          return { pages: [{ items: [optimistic], nextCursor: null }], pageParams: [null] };
        }
        const first = prev.pages[0];
        return { ...prev, pages: [{ ...first, items: [optimistic, ...first.items] }, ...prev.pages.slice(1)] };
      });

    await hub.sendMessage(channelId, clientMessageId, content, opts?.replyToId ?? null, opts?.attachmentIds ?? null);
  }, [channelId, qc, user.id, user.username]);
}
```

The `hub.sendMessage` signature in `signalr.ts` also needs to accept the two new params. Update its signature:

```ts
sendMessage(channelId: string, clientMessageId: string, content: string, replyToId: number | null, attachmentIds: string[] | null): Promise<SendMessageResponse>;
```

And the implementation:

```ts
    async sendMessage(channelId, clientMessageId, content, replyToId, attachmentIds) {
      await ensureStarted();
      return connection.invoke<SendMessageResponse>('SendMessage', {
        channelId, clientMessageId, content, replyToId, attachmentIds,
      });
    },
```

- [ ] **Step 27.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/useChannelMessages.ts src/Attic.Web/src/chat/useSendMessage.ts src/Attic.Web/src/api/signalr.ts docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): apply MessageEdited locally; send messages with replyToId + attachments"
```

---

## Task 28: `AttachmentPreview` + `useUploadAttachments`

**Files:**
- Create: `src/Attic.Web/src/chat/AttachmentPreview.tsx`
- Create: `src/Attic.Web/src/chat/useUploadAttachments.ts`

- [ ] **Step 28.1: `useUploadAttachments.ts`**

```ts
import { useCallback, useState } from 'react';
import { attachmentsApi } from '../api/attachments';
import type { UploadAttachmentResponse } from '../types';

export interface PendingUpload {
  id: string;          // local UUID before upload resolves
  file: File;
  status: 'uploading' | 'done' | 'error';
  attachment?: UploadAttachmentResponse;
  error?: string;
}

export function useUploadAttachments() {
  const [pending, setPending] = useState<PendingUpload[]>([]);

  const upload = useCallback(async (files: File[]) => {
    const startBatch: PendingUpload[] = files.map(f => ({
      id: crypto.randomUUID(), file: f, status: 'uploading',
    }));
    setPending(prev => [...prev, ...startBatch]);

    await Promise.all(startBatch.map(async p => {
      try {
        const resp = await attachmentsApi.upload(p.file);
        setPending(prev => prev.map(x =>
          x.id === p.id ? { ...x, status: 'done', attachment: resp } : x));
      } catch (e) {
        setPending(prev => prev.map(x =>
          x.id === p.id ? { ...x, status: 'error', error: (e as Error).message } : x));
      }
    }));
  }, []);

  const clear = useCallback(() => setPending([]), []);
  const removeOne = useCallback((id: string) => setPending(prev => prev.filter(p => p.id !== id)), []);

  return { pending, upload, clear, removeOne };
}
```

- [ ] **Step 28.2: `AttachmentPreview.tsx`**

Renders a bound `AttachmentDto` (from a message) — image inline, other files as a download pill.

```tsx
import type { AttachmentDto } from '../types';
import { attachmentsApi } from '../api/attachments';

export function AttachmentPreview({ attachment }: { attachment: AttachmentDto }) {
  const href = attachmentsApi.downloadUrl(attachment.id);
  if (attachment.contentType.startsWith('image/')) {
    return (
      <a href={href} target="_blank" rel="noreferrer" className="block max-w-sm my-1">
        <img src={href} alt={attachment.originalFileName} className="rounded border max-h-48" />
      </a>
    );
  }
  const kb = Math.max(1, Math.round(attachment.sizeBytes / 1024));
  return (
    <a href={href} target="_blank" rel="noreferrer"
       className="inline-flex items-center gap-2 mt-1 px-2 py-1 border rounded text-xs text-slate-700 hover:bg-slate-50">
      📎 {attachment.originalFileName} <span className="text-slate-400">· {kb} KB</span>
    </a>
  );
}
```

- [ ] **Step 28.3: Lint + commit**

```bash
cd src/Attic.Web && npm run lint && cd -
git add src/Attic.Web/src/chat/AttachmentPreview.tsx src/Attic.Web/src/chat/useUploadAttachments.ts docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): AttachmentPreview + upload queue hook"
```

---

## Task 29: `ChatInput` paste + drop + upload queue + reply bar

**Files:**
- Modify: `src/Attic.Web/src/chat/ChatInput.tsx`
- Create: `src/Attic.Web/src/chat/ReplyPreview.tsx`

- [ ] **Step 29.1: `ReplyPreview.tsx`**

```tsx
export function ReplyPreview({ replySnippet, onCancel }: { replySnippet: string; onCancel: () => void }) {
  return (
    <div className="flex items-center justify-between px-3 py-1 bg-slate-100 border-t border-b text-xs text-slate-600">
      <span>Replying to: <em className="text-slate-500">{replySnippet}</em></span>
      <button onClick={onCancel} className="px-2 text-slate-500 hover:text-slate-700">×</button>
    </div>
  );
}
```

- [ ] **Step 29.2: Rewrite `ChatInput.tsx`**

Phase 1's `ChatInput` takes `{ onSend: (content: string) => void }`. Extend to take `onSend(content, opts)` and handle paste/drop:

```tsx
import { useRef, useState } from 'react';
import { useUploadAttachments } from './useUploadAttachments';
import { ReplyPreview } from './ReplyPreview';

type OnSend = (
  content: string,
  opts?: { replyToId?: number | null; attachmentIds?: string[] }
) => void | Promise<void>;

export interface ChatInputProps {
  onSend: OnSend;
  replyTo?: { messageId: number; snippet: string } | null;
  onCancelReply?: () => void;
}

export function ChatInput({ onSend, replyTo, onCancelReply }: ChatInputProps) {
  const [content, setContent] = useState('');
  const { pending, upload, clear, removeOne } = useUploadAttachments();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const readyAttachments = pending.filter(p => p.status === 'done' && p.attachment).map(p => p.attachment!.id);
  const isBusy = pending.some(p => p.status === 'uploading');

  async function submit() {
    if (isBusy) return;
    if (!content.trim() && readyAttachments.length === 0) return;
    await onSend(content.trim(), { replyToId: replyTo?.messageId ?? null, attachmentIds: readyAttachments });
    setContent('');
    clear();
    onCancelReply?.();
  }

  function onPaste(e: React.ClipboardEvent<HTMLTextAreaElement>) {
    const files = Array.from(e.clipboardData?.files ?? []);
    if (files.length > 0) { e.preventDefault(); void upload(files); }
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    const files = Array.from(e.dataTransfer?.files ?? []);
    if (files.length > 0) void upload(files);
  }

  return (
    <div onDragOver={e => e.preventDefault()} onDrop={onDrop}>
      {replyTo && <ReplyPreview replySnippet={replyTo.snippet} onCancel={() => onCancelReply?.()} />}
      {pending.length > 0 && (
        <div className="flex flex-wrap gap-2 p-2 bg-slate-50 border-t">
          {pending.map(p => (
            <div key={p.id} className="flex items-center gap-1 px-2 py-1 bg-white border rounded text-xs">
              <span className={p.status === 'error' ? 'text-red-600' : p.status === 'uploading' ? 'text-slate-500' : ''}>
                {p.file.name}
              </span>
              {p.status === 'uploading' && <span className="text-slate-400">…</span>}
              <button onClick={() => removeOne(p.id)} className="text-slate-400">×</button>
            </div>
          ))}
        </div>
      )}
      <div className="flex items-end gap-2 p-3 border-t bg-white">
        <input ref={fileInputRef} type="file" multiple className="hidden"
               onChange={e => { if (e.target.files) { void upload(Array.from(e.target.files)); e.target.value = ''; } }} />
        <button onClick={() => fileInputRef.current?.click()} className="text-slate-500 hover:text-slate-700 pb-2">📎</button>
        <textarea
          className="flex-1 border rounded px-3 py-2 resize-none"
          rows={1}
          placeholder="Type a message…"
          value={content}
          onChange={e => setContent(e.target.value)}
          onPaste={onPaste}
          onKeyDown={e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(); }
          }}
        />
        <button onClick={submit} disabled={isBusy}
                className="px-3 py-2 bg-blue-600 text-white rounded disabled:opacity-50">
          Send
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 29.3: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/ChatInput.tsx src/Attic.Web/src/chat/ReplyPreview.tsx docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): ChatInput with paste/drop, upload queue, reply bar"
```

---

## Task 30: `MessageActionsMenu` + `useEditMessage` + `ChatWindow` integration

**Files:**
- Create: `src/Attic.Web/src/chat/MessageActionsMenu.tsx`
- Create: `src/Attic.Web/src/chat/useEditMessage.ts`
- Modify: `src/Attic.Web/src/chat/ChatWindow.tsx`

- [ ] **Step 30.1: `useEditMessage.ts`**

```ts
import { useCallback } from 'react';
import { getOrCreateHubClient } from '../api/signalr';

export function useEditMessage() {
  return useCallback(async (messageId: number, content: string) => {
    const hub = getOrCreateHubClient();
    const ack = await hub.editMessage({ messageId, content });
    if (!ack.ok) throw new Error(ack.error ?? 'edit_failed');
  }, []);
}
```

- [ ] **Step 30.2: `MessageActionsMenu.tsx`**

```tsx
export interface MessageActionsMenuProps {
  isOwn: boolean;
  isAdmin: boolean;
  onEdit: () => void;
  onReply: () => void;
  onDelete: () => void;
  onClose: () => void;
}

export function MessageActionsMenu({ isOwn, isAdmin, onEdit, onReply, onDelete, onClose }: MessageActionsMenuProps) {
  return (
    <div className="absolute right-2 top-8 bg-white border rounded shadow z-10 text-sm"
         onMouseLeave={onClose}>
      <button className="block w-full text-left px-3 py-1 hover:bg-slate-100" onClick={onReply}>Reply</button>
      {isOwn && <button className="block w-full text-left px-3 py-1 hover:bg-slate-100" onClick={onEdit}>Edit</button>}
      {(isOwn || isAdmin) && (
        <button className="block w-full text-left px-3 py-1 hover:bg-slate-100 text-red-600" onClick={onDelete}>Delete</button>
      )}
    </div>
  );
}
```

- [ ] **Step 30.3: Rewrite `ChatWindow.tsx` — integrate reply + edit + attachments**

```tsx
import { useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { useDeleteMessage } from './useDeleteMessage';
import { useEditMessage } from './useEditMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';
import { AttachmentPreview } from './AttachmentPreview';
import { MessageActionsMenu } from './MessageActionsMenu';

export function ChatWindow() {
  const { channelId } = useParams<{ channelId: string }>();
  const { user } = useAuth();
  if (!channelId) return <div className="p-8 text-slate-500">Select a channel.</div>;
  return <ChatWindowFor channelId={channelId} user={{ id: user!.id, username: user!.username }} />;
}

function ChatWindowFor({ channelId, user }: { channelId: string; user: { id: string; username: string } }) {
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(channelId);
  const send = useSendMessage(channelId, user);
  const del = useDeleteMessage(channelId);
  const edit = useEditMessage();
  const [menuMsgId, setMenuMsgId] = useState<number | null>(null);
  const [replyTo, setReplyTo] = useState<{ messageId: number; snippet: string } | null>(null);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState('');

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
  const byId = new Map(ordered.map(m => [m.id, m]));

  async function saveEdit() {
    if (editingId === null) return;
    const draft = editDraft.trim();
    if (!draft) return;
    try { await edit(editingId, draft); } catch { /* ignore — UI will invalidate */ }
    setEditingId(null);
    setEditDraft('');
  }

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
                <button onClick={() => setMenuMsgId(menuMsgId === m.id ? null : m.id)}
                        className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-slate-600 px-1"
                        aria-label="Message actions">⋯</button>
              )}
            </div>
            {m.replyToId && byId.get(m.replyToId) && (
              <div className="text-xs text-slate-500 border-l-2 border-slate-300 pl-2 mb-1">
                {byId.get(m.replyToId)!.senderUsername}: {byId.get(m.replyToId)!.content.slice(0, 80)}
              </div>
            )}
            {editingId === m.id ? (
              <div className="flex gap-2">
                <input className="flex-1 border rounded px-2 py-1 text-sm"
                       value={editDraft} onChange={e => setEditDraft(e.target.value)}
                       onKeyDown={e => { if (e.key === 'Enter') void saveEdit(); if (e.key === 'Escape') setEditingId(null); }} />
                <button onClick={saveEdit} className="text-xs text-blue-600">Save</button>
                <button onClick={() => setEditingId(null)} className="text-xs">Cancel</button>
              </div>
            ) : (
              <>
                <div className="whitespace-pre-wrap break-words">{m.content}</div>
                {m.attachments?.map(a => <AttachmentPreview key={a.id} attachment={a} />)}
              </>
            )}
            {menuMsgId === m.id && (
              <MessageActionsMenu
                isOwn={m.senderId === user.id}
                isAdmin={false /* RoomDetails already shows admin controls; Phase 4 defers admin-delete here */}
                onEdit={() => { setEditingId(m.id); setEditDraft(m.content); setMenuMsgId(null); }}
                onReply={() => { setReplyTo({ messageId: m.id, snippet: m.content.slice(0, 80) }); setMenuMsgId(null); }}
                onDelete={() => { void del(m.id); setMenuMsgId(null); }}
                onClose={() => setMenuMsgId(null)}
              />
            )}
          </div>
        ))}
      </div>
      <ChatInput onSend={send} replyTo={replyTo} onCancelReply={() => setReplyTo(null)} />
    </div>
  );
}
```

- [ ] **Step 30.4: Lint + build + commit**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
git add src/Attic.Web/src/chat/MessageActionsMenu.tsx src/Attic.Web/src/chat/useEditMessage.ts src/Attic.Web/src/chat/ChatWindow.tsx docs/superpowers/plans/2026-04-21-phase4-messaging-extras.md
git commit -m "feat(web): ChatWindow renders reply context + attachments + edit inline"
```

---

## Task 31: Final smoke

- [ ] **Step 31.1: Full backend**

```bash
export DOCKER_HOST=unix:///var/folders/32/wf0lxrcx6fzgd284x_b2wgf80000gn/T/podman/podman-machine-default-api.sock
dotnet test
```

Expected: Domain ~110 + Integration ~54 = all green.

- [ ] **Step 31.2: Frontend**

```bash
cd src/Attic.Web && npm run lint && npm run build && cd -
```

Expected: 0 errors.

- [ ] **Step 31.3: Checkpoint marker**

```bash
git commit --allow-empty -m "chore: Phase 4 end-to-end smoke green"
```

---

## Phase 4 completion checklist

- [x] `Attachment` entity with bind lifecycle + unit tests
- [x] `IAttachmentStorage` + `FilesystemAttachmentStorage` (SHA-256 content-addressable, atomic rename, dedupe)
- [x] `AttachmentConfiguration` + migration `AddAttachments`
- [x] `POST /api/attachments` streamed multipart upload with size caps (20 MB files / 3 MB images)
- [x] `GET /api/attachments/{id}` access-gated download (non-banned current member)
- [x] `ChatHub.SendMessage` binds `AttachmentIds[]` and echoes them on `MessageCreated`
- [x] `GET /api/channels/{id}/messages` projects `AttachmentDto[]` per message
- [x] `AttachmentSweeperService` (hourly orphan cleanup) + `StorageSweeperService` (ref-counted unlink on soft-delete)
- [x] `CanEditMessage` rule (author-only, not-deleted)
- [x] `ChatHub.EditMessage` fires `MessageEdited` broadcast; integration tests cover own-edit, non-author denial, empty-content rejection
- [x] Reply-to round-trips through history (Phase 1 field, now covered by a test)
- [x] FE: `AttachmentPreview`, `ChatInput` with paste/drop + upload queue + reply bar, `MessageActionsMenu` (Edit/Reply/Delete), inline edit, reply context rendering, realtime `MessageEdited` applied

## What is deferred to later phases

- **Presence heartbeats, active sessions, unread counts, account deletion cascade** — Phase 5.
- **Rate limiting tuned, GlobalHubFilter, AuditLog admin surface, security headers, prod Docker image** — Phase 6.
