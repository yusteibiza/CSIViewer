using CSIViewer.Media;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SystemDesktopStateTests
{
    [TestMethod]
    public void WithoutFreshCaptureOnlyNonMovingButtonAndKeyReleasesArePermitted()
    {
        var key = new InputEventPayload("key", "up", 0, 0, 0, 0, 0, 28, false);
        Assert.IsNotNull(SystemDesktopState.CleanupOnly(key));
        Assert.IsNull(SystemDesktopState.CleanupOnly(key with { Down = true }));
        Assert.IsNull(SystemDesktopState.CleanupOnly(key with { Kind = "text", Action = "synthetic" }));
        var mouse = new InputEventPayload("mouse", "leftUp", 50000, 50000, 0, 0, 0, 0, false);
        var release = SystemDesktopState.CleanupOnly(mouse);
        Assert.IsNotNull(release);
        Assert.IsFalse(release.HasPosition);
        Assert.AreEqual(0, release.X);
        Assert.IsNull(SystemDesktopState.CleanupOnly(mouse with { Action = "leftDown" }));
        Assert.IsNull(SystemDesktopState.CleanupOnly(mouse with { Action = "move" }));
    }

    [TestMethod]
    public void DesktopSwitchRequiresCaptureOfTheNewDesktopBeforeInput()
    {
        var time = 1000L;
        var state = new SystemDesktopState(() => time);
        Assert.IsFalse(state.CanInput("Default"));
        state.Captured("Default");
        Assert.IsTrue(state.CanInput("Default"));
        Assert.IsFalse(state.CanInput("Winlogon"));
        state.Captured("Winlogon");
        Assert.IsTrue(state.CanInput("Winlogon"));
        Assert.IsFalse(state.CanInput("Default"));
        Assert.IsFalse(state.CanInput(""));
    }

    [TestMethod]
    public void StaleOrInvalidCaptureTimeFailsClosed()
    {
        var time = 1000L;
        var state = new SystemDesktopState(() => time);
        state.Captured("Winlogon");
        time += 3000;
        Assert.IsFalse(state.CanInput("Winlogon"));
        time = 999;
        Assert.IsFalse(state.CanInput("Winlogon"));
    }
}
