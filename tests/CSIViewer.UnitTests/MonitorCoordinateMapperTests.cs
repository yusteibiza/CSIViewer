using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class MonitorCoordinateMapperTests
{
    [TestMethod]
    public void PrimaryMonitor_MapsCornersToVirtualDesktop()
    {
        var topLeft = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            0, 0, 1920, 1080, 0, 0, 1920, 1080, 0, 0);
        var bottomRight = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            0, 0, 1920, 1080, 0, 0, 1920, 1080, 65535, 65535);

        Assert.AreEqual((0, 0), topLeft);
        Assert.AreEqual((65535, 65535), bottomRight);
    }

    [TestMethod]
    public void MonitorToTheLeft_UsesNegativeOrigin()
    {
        // Virtual desktop: left monitor -1920..0, primary 0..1920.
        var leftCenter = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            -1920, 0, 1920, 1080, -1920, 0, 3840, 1080, 32768, 32768);
        var primaryLeft = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            0, 0, 1920, 1080, -1920, 0, 3840, 1080, 0, 0);

        Assert.IsTrue(leftCenter.X is > 15000 and < 18000);
        Assert.IsTrue(primaryLeft.X is > 32000 and < 34000);
    }

    [TestMethod]
    public void SecondMonitor_MapsToItsOwnRectangle()
    {
        var left = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            1920, 0, 1920, 1080, 0, 0, 3840, 1080, 0, 0);
        var right = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            1920, 0, 1920, 1080, 0, 0, 3840, 1080, 65535, 65535);

        Assert.IsTrue(left.X is > 32000 and < 33000);
        Assert.AreEqual(65535, right.X);
    }

    [TestMethod]
    public void NormalizedValues_AreClamped()
    {
        var result = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            0, 0, 1920, 1080, 0, 0, 1920, 1080, -100, 70000);

        Assert.AreEqual(0, result.X);
        Assert.AreEqual(65535, result.Y);
    }
}
