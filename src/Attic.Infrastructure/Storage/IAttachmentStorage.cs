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
