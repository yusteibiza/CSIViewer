using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ReconnectPolicyTests
{
    [TestMethod]
    public void ExponentialBackoff_IsCappedAtMaximum()
    {
        var policy = new ReconnectPolicy(
            maxRetries: 10,
            initialDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromSeconds(30));

        Assert.AreEqual(TimeSpan.FromSeconds(2), policy.GetBaseDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(4), policy.GetBaseDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(8), policy.GetBaseDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(16), policy.GetBaseDelay(4));
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.GetBaseDelay(5));
        Assert.AreEqual(TimeSpan.FromSeconds(30), policy.GetBaseDelay(20));
    }

    [TestMethod]
    public void RetryLimit_StopsAfterConfiguredRetries()
    {
        var limited = new ReconnectPolicy(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.IsTrue(limited.CanRetry(1));
        Assert.IsTrue(limited.CanRetry(3));
        Assert.IsFalse(limited.CanRetry(4));
        Assert.AreEqual(4, limited.MaxAttempts);

        var unlimited = new ReconnectPolicy(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.IsTrue(unlimited.IsUnlimited);
        Assert.IsTrue(unlimited.CanRetry(1000));
        Assert.AreEqual(0, unlimited.MaxAttempts);
    }

    [TestMethod]
    public void Jitter_StaysWithinConfiguredBounds()
    {
        var low = new ReconnectPolicy(
            5,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(60),
            jitterFactor: 0.2,
            random: () => 0.0);
        var neutral = new ReconnectPolicy(
            5,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(60),
            jitterFactor: 0.2,
            random: () => 0.5);
        var high = new ReconnectPolicy(
            5,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(60),
            jitterFactor: 0.2,
            random: () => 1.0);

        Assert.AreEqual(TimeSpan.FromSeconds(8), low.GetDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(10), neutral.GetDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(12), high.GetDelay(1));
        Assert.IsTrue(high.GetDelay(10) <= TimeSpan.FromSeconds(60));
    }

    [TestMethod]
    public void Delays_HaveASafeMinimum()
    {
        var policy = new ReconnectPolicy(
            3,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(5));

        Assert.IsTrue(policy.GetBaseDelay(1) >= TimeSpan.FromMilliseconds(100));
        Assert.IsTrue(policy.GetBaseDelay(2) >= TimeSpan.FromMilliseconds(100));
    }
}
