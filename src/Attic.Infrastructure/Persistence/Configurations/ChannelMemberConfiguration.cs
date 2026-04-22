using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelMemberConfiguration : IEntityTypeConfiguration<ChannelMember>
{
    public void Configure(EntityTypeBuilder<ChannelMember> b)
    {
        b.ToTable("channel_members");
        b.HasKey(cm => new { cm.ChannelId, cm.UserId });
        b.Property(cm => cm.Role).HasConversion<int>().IsRequired();
        b.Property(cm => cm.BanReason).HasMaxLength(512);
        b.HasIndex(cm => cm.UserId).HasDatabaseName("ix_channel_members_user");
        b.HasQueryFilter(cm => cm.BannedAt == null);

        b.HasOne<Channel>()
         .WithMany()
         .HasForeignKey(m => m.ChannelId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_channel_members_channel");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(m => m.UserId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channel_members_user");

        b.ToTable(t => t.HasCheckConstraint("ck_channel_members_role_enum", "role IN (0,1,2)"));
    }
}
