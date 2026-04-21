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
