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
    }
}
