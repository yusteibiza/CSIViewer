using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ReconnectCoordinatorTests
{
    private static ReconnectPolicy FastPolicy(int maxRetries = 3) =>
        new(maxRetries, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));

    private static Func<TimeSpan, CancellationToken, Task> InstantDelay(List<TimeSpan>? recorded = null) =>
        (delay, _) =>
        {
            recorded?.Add(delay);
            return Task.CompletedTask;
        };

    [TestMethod]
    public async Task Reconnects_AfterTransientFailures()
    {
        var delays = new List<TimeSpan>();
        var coordinator = new ReconnectCoordinator(FastPolicy(), InstantDelay(delays));
        var attempts = 0;

        var outcome = await coordinator.RunAsync(
            (_, _) => Task.FromResult(++attempts >= 3),
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(ReconnectOutcome.Connected, outcome);
        Assert.AreEqual(3, attempts);
        Assert.AreEqual(2, delays.Count);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), delays[0]);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), delays[1]);
    }

    [TestMethod]
    public async Task Stops_AfterMaxRetries()
    {
        var coordinator = new ReconnectCoordinator(
            FastPolicy(maxRetries: 2),
            InstantDelay());
        var attempts = 0;

        var outcome = await coordinator.RunAsync(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(false);
            },
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(ReconnectOutcome.Exhausted, outcome);
        Assert.AreEqual(3, attempts); // initial + 2 retries
    }

    [TestMethod]
    public async Task UnlimitedPolicy_StopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        var coordinator = new ReconnectCoordinator(
            FastPolicy(maxRetries: 0),
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var attempts = 0;

        var outcome = await coordinator.RunAsync(
            (_, _) =>
            {
                if (++attempts == 3)
                {
                    cts.Cancel();
                }

                return Task.FromResult(false);
            },
            cancellationToken: cts.Token);

        Assert.AreEqual(ReconnectOutcome.Cancelled, outcome);
        Assert.AreEqual(3, attempts);
    }

    [TestMethod]
    public async Task ConcurrentRuns_ReturnBusy()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ReconnectCoordinator(FastPolicy(), InstantDelay());

        var first = coordinator.RunAsync(
            async (_, _) =>
            {
                await gate.Task;
                return true;
            },
            cancellationToken: CancellationToken.None);

        await Task.Delay(50);
        var second = await coordinator.RunAsync(
            (_, _) => Task.FromResult(true),
            cancellationToken: CancellationToken.None);

        gate.SetResult(true);

        Assert.AreEqual(ReconnectOutcome.Busy, second);
        Assert.AreEqual(ReconnectOutcome.Connected, await first);
        Assert.IsFalse(coordinator.IsRunning);
    }

    [TestMethod]
    public async Task AttemptExceptions_AreTreatedAsRetryableFailures()
    {
        var failures = new List<int>();
        var coordinator = new ReconnectCoordinator(FastPolicy(), InstantDelay());
        var attempts = 0;

        var outcome = await coordinator.RunAsync(
            (attempt, _) =>
            {
                attempts++;
                if (attempt == 1)
                {
                    throw new IOException("network down");
                }

                return Task.FromResult(true);
            },
            onAttemptFailed: (_, attempt) => failures.Add(attempt),
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(ReconnectOutcome.Connected, outcome);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, failures.Count);
        Assert.AreEqual(1, failures[0]);
    }

    [TestMethod]
    public async Task AttemptOrder_IncludesInitialAttemptAndRetries()
    {
        var reported = new List<ReconnectAttemptInfo>();
        var coordinator = new ReconnectCoordinator(
            FastPolicy(maxRetries: 2),
            InstantDelay());
        var attempts = 0;

        await coordinator.RunAsync(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(attempts >= 3);
            },
            onAttempt: reported.Add,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(3, reported.Count);
        Assert.IsTrue(reported[0].IsInitialAttempt);
        Assert.IsFalse(reported[1].IsInitialAttempt);
        Assert.AreEqual(3, reported[2].Attempt);
    }
}
