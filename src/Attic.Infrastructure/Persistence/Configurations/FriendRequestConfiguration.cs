using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> b)
    {
        b.ToTable("friend_requests");
        b.HasKey(r => r.Id);
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.Text).HasMaxLength(500);

        b.HasIndex(r => new { r.RecipientId, r.Status })
            .HasDatabaseName("ix_friend_requests_recipient_status");

        b.HasIndex(r => new { r.SenderId, r.RecipientId })
            .IsUnique()
            .HasDatabaseName("ux_friend_requests_sender_recipient_pending")
            .HasFilter($"status = {(int)FriendRequestStatus.Pending}");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.SenderId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friend_requests_sender");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(r => r.RecipientId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friend_requests_recipient");

        b.ToTable(t => t.HasCheckConstraint("ck_friend_requests_status_enum", "status IN (0,1,2,3)"));
    }
}
