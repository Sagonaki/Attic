namespace Attic.Contracts.Attachments;

public sealed record AttachmentDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? Comment);
