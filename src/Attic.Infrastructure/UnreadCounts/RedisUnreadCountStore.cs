using StackExchange.Redis;

namespace Attic.Infrastructure.UnreadCounts;

public sealed class RedisUnreadCountStore(IConnectionMultiplexer connection) : IUnreadCountStore
{
    private static string Key(Guid userId, Guid channelId) => $"unread:user:{userId:D}:channel:{channelId:D}";

    public async Task<long> IncrementAsync(Guid userId, Guid channelId, CancellationToken ct)
        => await connection.GetDatabase().StringIncrementAsync(Key(userId, channelId));

    public Task ResetAsync(Guid userId, Guid channelId, CancellationToken ct)
        => connection.GetDatabase().StringSetAsync(Key(userId, channelId), 0);

    public async Task<long?> TryGetAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var v = await connection.GetDatabase().StringGetAsync(Key(userId, channelId));
        return v.HasValue && long.TryParse((string?)v, out var n) ? n : null;
    }

    public Task SetAsync(Guid userId, Guid channelId, long value, CancellationToken ct)
        => connection.GetDatabase().StringSetAsync(Key(userId, channelId), value);
}
