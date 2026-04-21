using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Attic.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AtticDbContext>
{
    // Used only by `dotnet-ef` at design time to generate migrations. Never opens a
    // connection — the connection string below is a placeholder for tool completeness.
    public AtticDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AtticDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=attic_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AtticDbContext(options);
    }
}
