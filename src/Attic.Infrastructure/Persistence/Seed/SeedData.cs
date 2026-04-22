using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a fixed set of QA-friendly demo data on startup: four demo users with known
/// credentials, four public rooms owned by the admin, opening messages, and two
/// friendships. Idempotent — each row is checked before insert, so the seed can run
/// on every boot without drift. Safe to call against a populated DB.
/// </summary>
public static class SeedData
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public record DemoUser(string Email, string Username, string Password);

    public static readonly DemoUser QaAdmin = new("qa-admin@attic.local", "qa-admin", "QaAdmin123!");
    public static readonly DemoUser Alice   = new("alice@attic.local",    "alice",    "Alice123!");
    public static readonly DemoUser Bob     = new("bob@attic.local",      "bob",      "Bob123!");
    public static readonly DemoUser Carol   = new("carol@attic.local",    "carol",    "Carol123!");

    public static readonly IReadOnlyList<DemoUser> DemoUsers = [QaAdmin, Alice, Bob, Carol];

    public static async Task EnsureSeededAsync(
        AtticDbContext db, IPasswordHasher hasher, CancellationToken ct)
    {
        // Users ------------------------------------------------------------------
        var userMap = new Dictionary<string, Guid>();
        foreach (var demo in DemoUsers)
        {
            var existing = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == demo.Email, ct);
            if (existing is not null) { userMap[demo.Email] = existing.Id; continue; }

            var user = User.Register(
                id: Guid.NewGuid(),
                email: demo.Email,
                username: demo.Username,
                passwordHash: hasher.Hash(demo.Password),
                createdAt: Epoch);
            db.Users.Add(user);
            userMap[demo.Email] = user.Id;
        }
        await db.SaveChangesAsync(ct);

        // Channels + memberships -------------------------------------------------
        var rooms = new[]
        {
            ("general",      "Everything that doesn't fit elsewhere."),
            ("random",       "Water cooler — no topic, low pressure."),
            ("engineering",  "Build issues, design questions, code review pings."),
            ("qa-feedback",  "Bug reports, repro steps, release readouts."),
        };

        var adminId = userMap[QaAdmin.Email];
        foreach (var (name, description) in rooms)
        {
            var channel = await db.Channels.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == name && c.Kind == ChannelKind.Public, ct);
            Guid channelId;
            if (channel is null)
            {
                var c = Channel.CreateRoom(
                    id: Guid.NewGuid(),
                    kind: ChannelKind.Public,
                    name: name,
                    description: description,
                    ownerId: adminId,
                    now: Epoch);
                db.Channels.Add(c);
                channelId = c.Id;
                db.ChannelMembers.Add(ChannelMember.Join(channelId, adminId, ChannelRole.Owner, Epoch));
            }
            else channelId = channel.Id;

            // Alice / Bob / Carol are members of general + random.
            if (name is "general" or "random")
            {
                foreach (var demo in new[] { Alice, Bob, Carol })
                {
                    var uid = userMap[demo.Email];
                    var joined = await db.ChannelMembers.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(m => m.ChannelId == channelId && m.UserId == uid, ct);
                    if (!joined)
                        db.ChannelMembers.Add(ChannelMember.Join(channelId, uid, ChannelRole.Member, Epoch));
                }
            }
        }
        await db.SaveChangesAsync(ct);

        // Seed messages (once, keyed on a known marker string so re-runs don't duplicate).
        const string Marker = "[seed] welcome to attic";
        var alreadySeededMessages = await db.Messages.AsNoTracking()
            .AnyAsync(m => m.Content == Marker, ct);
        if (!alreadySeededMessages)
        {
            var channelIds = await db.Channels.AsNoTracking()
                .Where(c => c.Name != null)
                .ToDictionaryAsync(c => c.Name!, c => c.Id, ct);

            void Post(Guid cid, Guid sender, string content) =>
                db.Messages.Add(Message.Post(cid, sender, content, null, Epoch));

            var gen = channelIds["general"];
            var eng = channelIds["engineering"];
            var qa  = channelIds["qa-feedback"];

            Post(gen, adminId, Marker);
            Post(gen, userMap[Alice.Email], "hey team — anyone around?");
            Post(gen, userMap[Bob.Email],   "morning!");
            Post(eng, adminId, "Phase 17 merged — send p95 down to 285 ms at 300 users.");
            Post(eng, userMap[Alice.Email], "nice. bumped max_connections?");
            Post(qa,  adminId, "Test matrix: register, send, edit, delete, attach.");
            Post(qa,  userMap[Carol.Email], "repro-steps doc landed in /docs — PTAL.");
            await db.SaveChangesAsync(ct);
        }

        // Friendships (alice↔bob, alice↔carol) — canonical unordered pair (UserAId < UserBId).
        static (Guid a, Guid b) Canonical(Guid x, Guid y) => x.CompareTo(y) < 0 ? (x, y) : (y, x);

        foreach (var (x, y) in new[] { (Alice, Bob), (Alice, Carol) })
        {
            var (a, b) = Canonical(userMap[x.Email], userMap[y.Email]);
            var exists = await db.Friendships.AsNoTracking()
                .AnyAsync(f => f.UserAId == a && f.UserBId == b, ct);
            if (!exists) db.Friendships.Add(Friendship.Create(a, b, Epoch));
        }
        await db.SaveChangesAsync(ct);
    }
}
