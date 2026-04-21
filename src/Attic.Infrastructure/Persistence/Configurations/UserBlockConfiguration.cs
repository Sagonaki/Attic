using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> b)
    {
        b.ToTable("user_blocks");
        b.HasKey(x => new { x.BlockerId, x.BlockedId });
        b.HasIndex(x => x.BlockedId).HasDatabaseName("ix_user_blocks_blocked");
    }
}
