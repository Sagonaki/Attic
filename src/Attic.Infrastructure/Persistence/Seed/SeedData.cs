namespace Attic.Infrastructure.Persistence.Seed;

/// <summary>
/// No-op in Phase 2. Users create their own channels via the REST API.
/// Kept as a static extension point for future phases (system accounts, default rooms, etc.).
/// </summary>
public static class SeedData
{
    public static Task EnsureSeededAsync(AtticDbContext db, CancellationToken ct) => Task.CompletedTask;
}
