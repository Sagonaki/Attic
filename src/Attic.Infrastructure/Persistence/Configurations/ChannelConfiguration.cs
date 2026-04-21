using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> b)
    {
        b.ToTable("channels");
        b.HasKey(c => c.Id);
        b.Property(c => c.Kind).HasConversion<int>().IsRequired();
        b.Property(c => c.Name).HasMaxLength(120);
        b.Property(c => c.Description).HasMaxLength(1024);
        b.HasQueryFilter(c => c.DeletedAt == null);

        b.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("ux_channels_name_not_personal")
            .HasFilter($"kind <> {(int)ChannelKind.Personal} AND deleted_at IS NULL")
            .IncludeProperties(c => new { c.Description, c.Kind });
    }
}
