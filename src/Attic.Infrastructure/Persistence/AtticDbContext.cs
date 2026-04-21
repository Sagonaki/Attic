using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence;

public sealed class AtticDbContext(DbContextOptions<AtticDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ChannelInvitation> ChannelInvitations => Set<ChannelInvitation>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ChannelRead> ChannelReads => Set<ChannelRead>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtticDbContext).Assembly);
    }
}
