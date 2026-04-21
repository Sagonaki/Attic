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
        group.MapPost("/", Upload).DisableAntiforgery()
             .RequireRateLimiting(Attic.Api.RateLimiting.RateLimitPolicyNames.UploadFixed);
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

        var abs = storage.Resolve(attachment.StoragePath);
        return Results.File(abs, attachment.ContentType, attachment.OriginalFileName,
            enableRangeProcessing: false);
        // Note: Results.File sets Content-Disposition: attachment automatically when fileDownloadName is provided.
        // For Phase 4 MVP, serving both images and files as attachment download is acceptable.
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
