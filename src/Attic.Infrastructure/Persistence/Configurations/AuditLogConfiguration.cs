using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).UseIdentityAlwaysColumn();
        b.Property(l => l.Action).HasMaxLength(64).IsRequired();
        b.Property(l => l.Data).HasColumnType("jsonb");

        b.HasIndex(l => l.CreatedAt).HasDatabaseName("ix_audit_logs_created");
        b.HasIndex(l => l.ActorUserId).HasDatabaseName("ix_audit_logs_actor");
        b.HasIndex(l => l.TargetChannelId).HasDatabaseName("ix_audit_logs_target_channel");

        // Actor FK — Restrict so audit survives soft-deleted users (tombstone remains reachable).
        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(l => l.ActorUserId)
         .OnDelete(DeleteBehavior.Restrict)
         .HasConstraintName("fk_audit_logs_actor");

        // Target lookups.
        b.HasIndex(l => l.TargetUserId)
         .HasDatabaseName("ix_audit_logs_target_user")
         .HasFilter("target_user_id IS NOT NULL");

        b.HasIndex(l => l.TargetMessageId)
         .HasDatabaseName("ix_audit_logs_target_message")
         .HasFilter("target_message_id IS NOT NULL");
    }
}
