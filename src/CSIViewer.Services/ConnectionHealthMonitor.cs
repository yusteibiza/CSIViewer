namespace CSIViewer.Services;

/// <summary>
/// Tracks the last time data was received from a connection. It is used both
/// for the signaling socket and for each remote session.
/// </summary>
public sealed class ConnectionHealthMonitor
{
    private long _lastActivityUtcTicks;

    public ConnectionHealthMonitor(DateTimeOffset? now = null)
    {
        _lastActivityUtcTicks = (now ?? DateTimeOffset.UtcNow).UtcTicks;
    }

    public DateTimeOffset LastActivityUtc =>
        new(Interlocked.Read(ref _lastActivityUtcTicks), TimeSpan.Zero);

    public void RecordActivity(DateTimeOffset now) =>
        Interlocked.Exchange(ref _lastActivityUtcTicks, now.UtcTicks);

    public TimeSpan TimeSinceLastActivity(DateTimeOffset now)
    {
        var elapsed = now.UtcTicks - Interlocked.Read(ref _lastActivityUtcTicks);
        return elapsed <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(elapsed);
    }

    public bool IsStale(DateTimeOffset now, TimeSpan timeout) =>
        timeout > TimeSpan.Zero && TimeSinceLastActivity(now) > timeout;
}
