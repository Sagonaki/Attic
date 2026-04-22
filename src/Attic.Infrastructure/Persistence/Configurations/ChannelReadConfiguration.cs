using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelReadConfiguration : IEntityTypeConfiguration<ChannelRead>
{
    public void Configure(EntityTypeBuilder<ChannelRead> b)
    {
        b.ToTable("channel_reads");
        b.HasKey(c => new { c.ChannelId, c.UserId });

        // Lookup by user for "how many unread across all my channels?" — secondary index.
        b.HasIndex(c => c.UserId).HasDatabaseName("ix_channel_reads_user");

        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(r => r.ChannelId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_reads_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.UserId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_reads_user");
    }
}
