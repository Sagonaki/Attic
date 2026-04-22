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

        // FK: owner_id → users.id (nullable; personal channels have null owner). RESTRICT because
        // account-delete hard-deletes owned channels' dependents before the user.
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(c => c.OwnerId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_channels_owner");

        // Partial index on owner_id for "my owned channels" lookups.
        b.HasIndex(c => c.OwnerId)
         .HasDatabaseName("ix_channels_owner")
         .HasFilter("owner_id IS NOT NULL");

        // CHECK: kind ∈ {0,1,2}.
        b.ToTable(t => t.HasCheckConstraint("ck_channels_kind_enum", "kind IN (0,1,2)"));
    }
}
