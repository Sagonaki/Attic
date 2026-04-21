using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).UseIdentityAlwaysColumn();
        b.Property(m => m.Content).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("ck_messages_content_length", "octet_length(content) <= 3072"));
        b.HasQueryFilter(m => m.DeletedAt == null);

        b.HasIndex(m => new { m.ChannelId, m.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_channel_id_desc");

        b.HasIndex(m => new { m.SenderId, m.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_sender_id_desc");
    }
}
