using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence.Seed;

public static class SeedData
{
    public static readonly Guid LobbyChannelId = Guid.Parse("11111111-1111-1111-1111-000000000001");

    public static async Task EnsureSeededAsync(AtticDbContext db, CancellationToken ct)
    {
        var exists = await db.Channels.AnyAsync(c => c.Id == LobbyChannelId, ct);
        if (exists) return;

        var now = DateTimeOffset.UtcNow;
        var channel = Channel.CreateRoom(
            LobbyChannelId,
            ChannelKind.Public,
            "lobby",
            "The default public channel. Say hi.",
            ownerId: Guid.Empty,       // no owner in Phase 1; revisited in Phase 2 when users create their own
            now);
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
    }
}
