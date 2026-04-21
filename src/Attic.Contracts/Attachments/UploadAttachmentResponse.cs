namespace Attic.Contracts.Attachments;

public sealed record UploadAttachmentResponse(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);
