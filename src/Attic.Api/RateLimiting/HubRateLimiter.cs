using System.Collections.Concurrent;

namespace Attic.Api.RateLimiting;

public sealed class HubRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private const int MaxCalls = 60;

    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _history = new();

    public bool TryAcquire(Guid userId, DateTimeOffset now)
    {
        var queue = _history.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            var cutoff = now - Window;
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= MaxCalls) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
