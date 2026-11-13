using System.Collections.Concurrent;

namespace CSIViewer.Server;

/// <summary>
/// Sliding-window registration throttle per remote IP address.
/// Legitimate clients behind the same NAT may register several devices in a
/// burst (for example ten controllers and a host starting together), so the
/// default burst is deliberately higher than the maximum number of concurrent
/// sessions. The enrollment code remains the primary authorization gate.
/// </summary>
public sealed class RegistrationThrottle
{
    public const int DefaultMaxAttempts = 64;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private long _operations;

    public RegistrationThrottle()
        : this(DefaultMaxAttempts, DefaultWindow)
    {
    }

    public RegistrationThrottle(int maxAttempts, TimeSpan window)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _window = window <= TimeSpan.Zero ? DefaultWindow : window;
    }

    public bool TryAcquire(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > _window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= _maxAttempts)
            {
                return false;
            }

            queue.Enqueue(now);
        }

        // Avoid unbounded growth when many distinct addresses attempt to register.
        if (Interlocked.Increment(ref _operations) % 256 == 0)
        {
            Prune(now);
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
