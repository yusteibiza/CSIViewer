using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ConnectionHealthMonitorTests
{
    [TestMethod]
    public void FreshMonitor_IsHealthy()
    {
        var now = DateTimeOffset.UtcNow;
        var monitor = new ConnectionHealthMonitor(now);

        Assert.IsFalse(monitor.IsStale(now.AddSeconds(5), TimeSpan.FromSeconds(20)));
        Assert.AreEqual(TimeSpan.FromSeconds(5), monitor.TimeSinceLastActivity(now.AddSeconds(5)));
    }

    [TestMethod]
    public void Monitor_BecomesStaleAfterTimeout()
    {
        var now = DateTimeOffset.UtcNow;
        var monitor = new ConnectionHealthMonitor(now);

        Assert.IsFalse(monitor.IsStale(now.AddSeconds(20), TimeSpan.FromSeconds(20)));
        Assert.IsTrue(monitor.IsStale(now.AddSeconds(21), TimeSpan.FromSeconds(20)));
    }

    [TestMethod]
    public void Activity_ResetsTheWatchdog()
    {
        var now = DateTimeOffset.UtcNow;
        var monitor = new ConnectionHealthMonitor(now);

        monitor.RecordActivity(now.AddSeconds(15));

        Assert.IsFalse(monitor.IsStale(now.AddSeconds(30), TimeSpan.FromSeconds(20)));
        Assert.IsTrue(monitor.IsStale(now.AddSeconds(36), TimeSpan.FromSeconds(20)));
    }

    [TestMethod]
    public void TimeSinceActivity_IsNeverNegative()
    {
        var now = DateTimeOffset.UtcNow;
        var monitor = new ConnectionHealthMonitor(now.AddSeconds(10));

        Assert.AreEqual(TimeSpan.Zero, monitor.TimeSinceLastActivity(now));
    }
}
