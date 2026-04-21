using System.Security.Cryptography;
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
