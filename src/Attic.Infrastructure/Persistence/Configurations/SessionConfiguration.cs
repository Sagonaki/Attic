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

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(s => s.UserId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_sessions_user");

        // Auth lookup path: WHERE token_hash = @hash. Make it unique + indexed.
        b.HasIndex(s => s.TokenHash)
         .IsUnique()
         .HasDatabaseName("ux_sessions_token_hash");

        // Cleanup path: WHERE revoked_at IS NULL AND expires_at > now(). Partial on revoked to keep it tight.
        b.HasIndex(s => s.ExpiresAt)
         .HasDatabaseName("ix_sessions_expires_at")
         .HasFilter("revoked_at IS NULL");
    }
}
