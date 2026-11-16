using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class FrameContentProbeTests
{
    [TestMethod]
    public void FirstValidFrameChangesThenRepeatedPixelsDoNot()
    {
        var probe = new FrameContentProbe();
        var pixels = new byte[16 * 16 * 4];
        Assert.IsTrue(probe.TrySample(pixels, 16, 16, out var changed, out var samples));
        Assert.IsTrue(changed);
        Assert.AreEqual(256, samples);
        probe.TrySample(pixels, 16, 16, out changed, out _);
        Assert.IsFalse(changed);
        pixels[0] = 1;
        probe.TrySample(pixels, 16, 16, out changed, out _);
        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void AlphaOnlyChangesDoNotImplyDesktopRgbProgress()
    {
        var probe = new FrameContentProbe();
        var pixels = new byte[4];
        probe.TrySample(pixels, 1, 1, out _, out _);
        pixels[3] = 255;
        probe.TrySample(pixels, 1, 1, out var changed, out _);
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void InvalidDimensionsOrTruncatedDataAreRejectedWithoutChangingState()
    {
        var probe = new FrameContentProbe();
        var pixels = new byte[16];
        probe.TrySample(pixels, 2, 2, out _, out _);
        Assert.IsFalse(probe.TrySample(pixels, 3, 2, out _, out _));
        Assert.IsFalse(probe.TrySample(pixels, -1, 2, out _, out _));
        Assert.IsFalse(probe.TrySample(pixels, int.MaxValue, int.MaxValue, out _, out _));
        probe.TrySample(pixels, 2, 2, out var changed, out _);
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void DimensionChangesAreReportedEvenForSolidColor()
    {
        var probe = new FrameContentProbe();
        probe.TrySample(new byte[16], 2, 2, out _, out _);
        probe.TrySample(new byte[32], 4, 2, out var changed, out _);
        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void SamplingCostIsBoundedAndOffGridChangesCanBeMissed()
    {
        var probe = new FrameContentProbe();
        var pixels = new byte[2560 * 1440 * 4];
        probe.TrySample(pixels, 2560, 1440, out _, out var samples);
        Assert.AreEqual(64 * 36, samples);
        pixels[0] = 255; // (0,0) is outside the centered sampling grid
        probe.TrySample(pixels, 2560, 1440, out var changed, out _);
        Assert.IsFalse(changed, "The signal is sampled progress, not proof of whole-frame equality.");
    }

    [TestMethod]
    public void ProbeExposesNoPixelsOrFingerprintAsPublicProperties()
    {
        Assert.IsEmpty(typeof(FrameContentProbe).GetProperties());
        Assert.IsEmpty(typeof(FrameContentProbe).GetFields());
    }
}
