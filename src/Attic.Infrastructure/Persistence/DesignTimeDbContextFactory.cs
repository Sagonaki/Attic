using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Attic.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AtticDbContext>
{
    public AtticDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AtticDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=attic_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        var interceptor = new TimestampInterceptor(new Clock.SystemClock());
        return new AtticDbContext(options, interceptor);
    }
}
