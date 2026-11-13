using CSIViewer.Application;

namespace CSIViewer.Services;

/// <summary>
/// Exponential backoff policy for server reconnection. The first connection
/// attempt is immediate; delays apply only to retries.
/// </summary>
public sealed class ReconnectPolicy
{
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);

    private readonly double _jitterFactor;
    private readonly Func<double> _random;

    public ReconnectPolicy(
        int maxRetries,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        double jitterFactor = 0,
        Func<double>? random = null)
    {
        MaxRetries = Math.Max(0, maxRetries);
        InitialDelay = initialDelay < MinimumDelay ? MinimumDelay : initialDelay;
        MaxDelay = maxDelay < InitialDelay ? InitialDelay : maxDelay;
        _jitterFactor = Math.Clamp(jitterFactor, 0, 0.5);
        _random = random ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>Number of retries after the first attempt. Zero means unlimited.</summary>
    public int MaxRetries { get; }

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaxDelay { get; }

    public bool IsUnlimited => MaxRetries == 0;

    public int MaxAttempts => IsUnlimited ? 0 : MaxRetries + 1;

    public bool CanRetry(int retryIndex) =>
        retryIndex >= 1 && (IsUnlimited || retryIndex <= MaxRetries);

    /// <summary>Delay without jitter, capped at <see cref="MaxDelay"/>.</summary>
    public TimeSpan GetBaseDelay(int retryIndex)
    {
        if (retryIndex <= 0)
        {
            return TimeSpan.Zero;
        }

        // Avoid overflow and absurd exponentials.
        var exponent = Math.Min(20, retryIndex - 1);
        var factor = Math.Pow(2, exponent);
        var ticks = InitialDelay.Ticks * factor;
        if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks >= MaxDelay.Ticks)
        {
            return MaxDelay;
        }

        return TimeSpan.FromTicks(Math.Max(MinimumDelay.Ticks, (long)ticks));
    }

    /// <summary>Delay with symmetric jitter in [-factor, +factor].</summary>
    public TimeSpan GetDelay(int retryIndex)
    {
        var baseDelay = GetBaseDelay(retryIndex);
        if (_jitterFactor <= 0 || baseDelay <= MinimumDelay)
        {
            return baseDelay;
        }

        var jitter = (_random() * 2 - 1) * _jitterFactor;
        var ticks = baseDelay.Ticks * (1 + jitter);
        return TimeSpan.FromTicks(Math.Clamp(
            (long)ticks,
            MinimumDelay.Ticks,
            MaxDelay.Ticks));
    }

    public static ReconnectPolicy FromSettings(AppSettings settings) =>
        new(
            settings.ReconnectMaxRetries,
            TimeSpan.FromSeconds(settings.ReconnectInitialDelaySeconds),
            TimeSpan.FromSeconds(settings.ReconnectMaxDelaySeconds),
            settings.ReconnectJitterPercent / 100.0);
}
