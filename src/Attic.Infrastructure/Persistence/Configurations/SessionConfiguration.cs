using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(s => s.UserAgent).HasMaxLength(512);
        b.Property(s => s.Ip).HasMaxLength(64);
        b.HasIndex(s => new { s.UserId })
            .HasDatabaseName("ix_sessions_active")
            .HasFilter("revoked_at IS NULL");
    }
}
