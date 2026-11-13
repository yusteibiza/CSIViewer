namespace CSIViewer.Services;

public readonly record struct ReconnectAttemptInfo(
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    bool IsInitialAttempt);

public enum ReconnectOutcome
{
    Connected,
    Exhausted,
    Cancelled,
    Busy
}

/// <summary>
/// Single-flight reconnect runner. It prevents two reconnection loops from
/// running at the same time, applies the backoff policy and exposes attempt
/// information so the UI can show progress without blocking.
/// </summary>
public sealed class ReconnectCoordinator
{
    private readonly ReconnectPolicy _policy;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _running;

    public ReconnectCoordinator(
        ReconnectPolicy policy,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _delay = delay ?? ((time, token) => Task.Delay(time, token));
    }

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task<ReconnectOutcome> RunAsync(
        Func<int, CancellationToken, Task<bool>> attemptAsync,
        Action<ReconnectAttemptInfo>? onAttempt = null,
        Action<Exception, int>? onAttemptFailed = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return ReconnectOutcome.Busy;
        }

        try
        {
            onAttempt?.Invoke(new ReconnectAttemptInfo(
                1,
                _policy.MaxRetries,
                TimeSpan.Zero,
                IsInitialAttempt: true));

            if (await TryAttemptAsync(attemptAsync, 1, onAttemptFailed, cancellationToken)
                    .ConfigureAwait(false))
            {
                return ReconnectOutcome.Connected;
            }

            for (var retry = 1; _policy.CanRetry(retry); retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delay = _policy.GetDelay(retry);
                onAttempt?.Invoke(new ReconnectAttemptInfo(
                    retry + 1,
                    _policy.MaxRetries,
                    delay,
                    IsInitialAttempt: false));

                await _delay(delay, cancellationToken).ConfigureAwait(false);

                if (await TryAttemptAsync(attemptAsync, retry + 1, onAttemptFailed, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return ReconnectOutcome.Connected;
                }
            }

            // An unlimited policy only stops through cancellation or success.
            return _policy.IsUnlimited
                ? ReconnectOutcome.Cancelled
                : ReconnectOutcome.Exhausted;
        }
        catch (OperationCanceledException)
        {
            return ReconnectOutcome.Cancelled;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task<bool> TryAttemptAsync(
        Func<int, CancellationToken, Task<bool>> attemptAsync,
        int attempt,
        Action<Exception, int>? onAttemptFailed,
        CancellationToken cancellationToken)
    {
        try
        {
            return await attemptAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            onAttemptFailed?.Invoke(ex, attempt);
            return false;
        }
    }
}
