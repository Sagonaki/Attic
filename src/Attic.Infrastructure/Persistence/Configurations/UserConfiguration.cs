using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).IsRequired().HasMaxLength(320);
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.Username).IsRequired().HasMaxLength(32);
        b.HasIndex(u => u.Username).IsUnique();
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.CreatedAt).IsRequired();
        b.HasQueryFilter(u => u.DeletedAt == null);
    }
}
