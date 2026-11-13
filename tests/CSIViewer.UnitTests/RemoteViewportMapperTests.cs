using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class RemoteViewportMapperTests
{
    [TestMethod]
    public void SameAspectRatio_MapsFullSurface()
    {
        Assert.IsTrue(RemoteViewportMapper.TryMapToNormalized(
            1920, 1080, 1920, 1080, 960, 540, out var nx, out var ny));

        Assert.AreEqual(0.5, nx, 0.001);
        Assert.AreEqual(0.5, ny, 0.001);
    }

    [TestMethod]
    public void WideImage_LetterboxesVertically()
    {
        // 16:9 image in a 4:3 surface: top/bottom bars.
        Assert.IsFalse(RemoteViewportMapper.TryMapToNormalized(
            1000, 1000, 1920, 1080, 500, 50, out _, out _));
        Assert.IsTrue(RemoteViewportMapper.TryMapToNormalized(
            1000, 1000, 1920, 1080, 500, 500, out var nx, out var ny));
        Assert.AreEqual(0.5, nx, 0.001);
        Assert.AreEqual(0.5, ny, 0.001);
    }

    [TestMethod]
    public void TallImage_PillarboxesHorizontally()
    {
        // 3:4 image in a wide surface: left/right bars.
        Assert.IsFalse(RemoteViewportMapper.TryMapToNormalized(
            1600, 900, 900, 1200, 50, 450, out _, out _));
        Assert.IsTrue(RemoteViewportMapper.TryMapToNormalized(
            1600, 900, 900, 1200, 800, 450, out var nx, out var ny));
        Assert.AreEqual(0.5, nx, 0.001);
        Assert.AreEqual(0.5, ny, 0.001);
    }

    [TestMethod]
    public void OutsideImage_IsRejected()
    {
        Assert.IsFalse(RemoteViewportMapper.TryMapToNormalized(
            1000, 1000, 1920, 1080, -1, 500, out _, out _));
        Assert.IsFalse(RemoteViewportMapper.TryMapToNormalized(
            1000, 1000, 1920, 1080, 1001, 500, out _, out _));
    }

    [TestMethod]
    public void MissingSourceSize_UsesSurface()
    {
        Assert.IsTrue(RemoteViewportMapper.TryMapToNormalized(
            800, 600, 0, 0, 400, 300, out var nx, out var ny));
        Assert.AreEqual(0.5, nx, 0.001);
        Assert.AreEqual(0.5, ny, 0.001);
    }
}
