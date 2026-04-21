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
