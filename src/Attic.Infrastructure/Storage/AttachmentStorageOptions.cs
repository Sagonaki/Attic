namespace Attic.Infrastructure.Storage;

public sealed class AttachmentStorageOptions
{
    /// <summary>Root directory. In dev: a relative path under the project's working dir. In prod: a mounted volume.</summary>
    public string Root { get; set; } = "data/attachments";
    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024;
    public long MaxImageBytes { get; set; } = 3L * 1024 * 1024;
}
