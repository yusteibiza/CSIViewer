using System.Collections.Concurrent;

namespace CSIViewer.Networking;

/// <summary>
/// Thread-safe sliding-window limiter keyed by device, IP or any caller-defined
/// string. Used to bound connection attempts and other abusable operations.
/// </summary>
public sealed class SlidingWindowThrottle
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts =
        new(StringComparer.Ordinal);
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private long _operations;

    public SlidingWindowThrottle(int maxAttempts, TimeSpan window)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _window = window <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : window;
    }

    public bool IsLimited(string key, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        if (!_attempts.TryGetValue(key, out var queue))
        {
            return false;
        }

        lock (queue)
        {
            while (queue.Count > 0 && current - queue.Peek() > _window)
            {
                queue.Dequeue();
            }

            return queue.Count >= _maxAttempts;
        }
    }

    public bool TryAcquire(string key, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        var queue = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && current - queue.Peek() > _window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= _maxAttempts)
            {
                return false;
            }

            queue.Enqueue(current);
        }

        if (Interlocked.Increment(ref _operations) % 256 == 0)
        {
            Prune(current);
        }

        return true;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var entry in _attempts)
        {
            lock (entry.Value)
            {
                while (entry.Value.Count > 0 && now - entry.Value.Peek() > _window)
                {
                    entry.Value.Dequeue();
                }

                if (entry.Value.Count == 0)
                {
                    _attempts.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}
