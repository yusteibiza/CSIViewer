using CSIViewer.Networking;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SlidingWindowThrottleTests
{
    [TestMethod]
    public void LimitsAttemptsWithinTheWindow()
    {
        var throttle = new SlidingWindowThrottle(3, TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.UtcNow;

        Assert.IsTrue(throttle.TryAcquire("device-a", now));
        Assert.IsTrue(throttle.TryAcquire("device-a", now.AddSeconds(1)));
        Assert.IsTrue(throttle.TryAcquire("device-a", now.AddSeconds(2)));
        Assert.IsFalse(throttle.TryAcquire("device-a", now.AddSeconds(3)));
        Assert.IsTrue(throttle.TryAcquire("device-b", now.AddSeconds(3)));
    }

    [TestMethod]
    public void IsLimited_DoesNotCountAsAnAttempt()
    {
        var throttle = new SlidingWindowThrottle(2, TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.UtcNow;

        Assert.IsFalse(throttle.IsLimited("device-a", now));
        Assert.IsTrue(throttle.TryAcquire("device-a", now));
        Assert.IsTrue(throttle.TryAcquire("device-a", now));
        Assert.IsTrue(throttle.IsLimited("device-a", now));
        Assert.IsFalse(throttle.TryAcquire("device-a", now));
    }

    [TestMethod]
    public void WindowExpiry_AllowsNewAttempts()
    {
        var throttle = new SlidingWindowThrottle(2, TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        Assert.IsTrue(throttle.TryAcquire("device-a", now));
        Assert.IsTrue(throttle.TryAcquire("device-a", now.AddSeconds(1)));
        Assert.IsFalse(throttle.TryAcquire("device-a", now.AddSeconds(2)));
        Assert.IsTrue(throttle.TryAcquire("device-a", now.AddSeconds(20)));
    }
}
