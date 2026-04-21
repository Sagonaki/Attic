using Attic.Domain.Entities;
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence;

public sealed class AtticDbContext(DbContextOptions<AtticDbContext> options, TimestampInterceptor interceptor) : DbContext(options)
{
    private readonly TimestampInterceptor _interceptor = interceptor;

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_interceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtticDbContext).Assembly);
    }
}
