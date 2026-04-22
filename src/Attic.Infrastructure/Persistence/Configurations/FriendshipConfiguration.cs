using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> b)
    {
        b.ToTable("friendships", t => t.HasCheckConstraint("ck_friendships_user_order", "user_a_id < user_b_id"));
        b.HasKey(f => new { f.UserAId, f.UserBId });

        b.HasIndex(f => f.UserAId).HasDatabaseName("ix_friendships_user_a");
        b.HasIndex(f => f.UserBId).HasDatabaseName("ix_friendships_user_b");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(f => f.UserAId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friendships_user_a");

        b.HasOne<User>()
         .WithMany()
         .HasForeignKey(f => f.UserBId)
         .OnDelete(DeleteBehavior.Cascade)
         .HasConstraintName("fk_friendships_user_b");
    }
}
