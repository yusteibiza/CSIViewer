namespace CSIViewer.Services;

/// <summary>
/// Per-device authentication failure tracker. A global lockout lets one
/// attacker deny service to every legitimate controller, so failures are
/// tracked by the authenticated controller identity instead.
/// </summary>
public sealed class AuthenticationAttemptTracker
{
    private sealed class Entry
    {
        public Queue<DateTimeOffset> Failures { get; } = new();
        public DateTimeOffset LockedUntil { get; set; }
        public int LockoutLevel { get; set; }
        public DateTimeOffset LastSeen { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _baseLockout;
    private readonly TimeSpan _maxLockout;
    private long _operations;

    public AuthenticationAttemptTracker(
        int maxFailures = 5,
        TimeSpan? window = null,
        TimeSpan? baseLockout = null,
        TimeSpan? maxLockout = null)
    {
        _maxFailures = Math.Max(1, maxFailures);
        _window = window ?? TimeSpan.FromMinutes(10);
        _baseLockout = baseLockout ?? TimeSpan.FromSeconds(30);
        _maxLockout = maxLockout ?? TimeSpan.FromMinutes(5);
    }

    public bool IsLocked(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            return TryGetEntry(key, now) is { } entry && entry.LockedUntil > now;
        }
    }

    public TimeSpan? RemainingLockout(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (TryGetEntry(key, now) is { } entry && entry.LockedUntil > now)
            {
                return entry.LockedUntil - now;
            }

            return null;
        }
    }

    /// <summary>Records a failure and returns true when the key is now locked.</summary>
    public bool RecordFailure(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.LastSeen = now;
            while (entry.Failures.Count > 0 && now - entry.Failures.Peek() > _window)
            {
                entry.Failures.Dequeue();
            }

            entry.Failures.Enqueue(now);
            if (entry.Failures.Count < _maxFailures)
            {
                return false;
            }

            var exponent = Math.Min(6, entry.LockoutLevel);
            var lockoutTicks = _baseLockout.Ticks * Math.Pow(2, exponent);
            var lockout = TimeSpan.FromTicks((long)Math.Min(_maxLockout.Ticks, lockoutTicks));
            entry.LockedUntil = now + lockout;
            entry.LockoutLevel++;
            entry.Failures.Clear();
            return true;
        }
    }

    public void RecordSuccess(string key)
    {
        lock (_gate)
        {
            _entries.Remove(key);
        }
    }

    private Entry? TryGetEntry(string key, DateTimeOffset now)
    {
        Prune(now);
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    private void Prune(DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _operations) % 256 != 0)
        {
            return;
        }

        foreach (var entry in _entries.ToArray())
        {
            if (now - entry.Value.LastSeen > TimeSpan.FromHours(1) &&
                entry.Value.LockedUntil <= now &&
                entry.Value.Failures.Count == 0)
            {
                _entries.Remove(entry.Key);
            }
        }
    }
}
