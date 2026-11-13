using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class AuthenticationAttemptTrackerTests
{
    [TestMethod]
    public void FiveFailures_LockOnlyThatDevice()
    {
        var tracker = new AuthenticationAttemptTracker();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 4; i++)
        {
            Assert.IsFalse(tracker.RecordFailure("controller-a", now.AddSeconds(i)));
        }

        Assert.IsTrue(tracker.RecordFailure("controller-a", now.AddSeconds(5)));
        Assert.IsTrue(tracker.IsLocked("controller-a", now.AddSeconds(6)));
        Assert.IsFalse(tracker.IsLocked("controller-b", now.AddSeconds(6)));
        Assert.IsNotNull(tracker.RemainingLockout("controller-a", now.AddSeconds(6)));
    }

    [TestMethod]
    public void SuccessfulAuthentication_ClearsFailures()
    {
        var tracker = new AuthenticationAttemptTracker();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            tracker.RecordFailure("controller-a", now.AddSeconds(i));
        }

        tracker.RecordSuccess("controller-a");

        Assert.IsFalse(tracker.IsLocked("controller-a", now.AddSeconds(10)));
        Assert.IsFalse(tracker.RecordFailure("controller-a", now.AddSeconds(10)));
    }

    [TestMethod]
    public void RepeatedLockouts_IncreaseTheDelay()
    {
        var tracker = new AuthenticationAttemptTracker(
            maxFailures: 5,
            baseLockout: TimeSpan.FromSeconds(30),
            maxLockout: TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordFailure("controller-a", now.AddSeconds(i));
        }

        Assert.IsTrue(tracker.IsLocked("controller-a", now.AddSeconds(6)));
        now = now.AddSeconds(40);
        Assert.IsFalse(tracker.IsLocked("controller-a", now));

        for (var i = 0; i < 5; i++)
        {
            tracker.RecordFailure("controller-a", now.AddSeconds(i));
        }

        var remaining = tracker.RemainingLockout("controller-a", now.AddSeconds(6));
        Assert.IsNotNull(remaining);
        Assert.IsTrue(remaining.Value > TimeSpan.FromSeconds(45));
    }

    [TestMethod]
    public void LockoutExpires()
    {
        var tracker = new AuthenticationAttemptTracker(
            baseLockout: TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordFailure("controller-a", now.AddSeconds(i));
        }

        Assert.IsTrue(tracker.IsLocked("controller-a", now.AddSeconds(6)));
        Assert.IsFalse(tracker.IsLocked("controller-a", now.AddSeconds(30)));
    }
}
