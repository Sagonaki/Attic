using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelInvitationConfiguration : IEntityTypeConfiguration<ChannelInvitation>
{
    public void Configure(EntityTypeBuilder<ChannelInvitation> b)
    {
        b.ToTable("channel_invitations");
        b.HasKey(i => i.Id);
        b.Property(i => i.Status).HasConversion<int>().IsRequired();

        b.HasIndex(i => new { i.InviteeId, i.Status })
            .HasDatabaseName("ix_channel_invitations_invitee_status");

        b.HasIndex(i => new { i.ChannelId, i.InviteeId })
            .IsUnique()
            .HasDatabaseName("ux_channel_invitations_channel_invitee_pending")
            .HasFilter($"status = {(int)InvitationStatus.Pending}");
    }
}
