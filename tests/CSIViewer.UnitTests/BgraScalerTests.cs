using CSIViewer.Media;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class BgraScalerTests
{
    [TestMethod]
    public void Downscale_UniformImage_KeepsColor()
    {
        var source = CreateImage(8, 8, (byte)40, (byte)120, (byte)200, (byte)255);
        var target = BgraScaler.Downscale(source, 8, 8, 4);

        Assert.AreEqual(4 * 4 * 4, target.Length);
        for (var index = 0; index < target.Length; index += 4)
        {
            Assert.AreEqual(40, target[index]);
            Assert.AreEqual(120, target[index + 1]);
            Assert.AreEqual(200, target[index + 2]);
            Assert.AreEqual(255, target[index + 3]);
        }
    }

    [TestMethod]
    public void Downscale_Checkerboard_AveragesSourcePixels()
    {
        // A nearest-neighbour scaler would keep either black or white. The box
        // filter must average the four source pixels into mid grey.
        var source = new byte[2 * 2 * 4];
        SetPixel(source, 2, 0, 0, 0, 0, 0, 255);
        SetPixel(source, 2, 1, 0, 255, 255, 255, 255);
        SetPixel(source, 2, 0, 1, 255, 255, 255, 255);
        SetPixel(source, 2, 1, 1, 0, 0, 0, 255);

        var target = BgraScaler.Downscale(source, 2, 2, 1);

        Assert.AreEqual(4, target.Length);
        Assert.AreEqual(127, target[0]);
        Assert.AreEqual(127, target[1]);
        Assert.AreEqual(127, target[2]);
        Assert.AreEqual(255, target[3]);
    }

    [TestMethod]
    public void Downscale_SameWidth_ReturnsOriginalPixels()
    {
        var source = CreateImage(4, 3, 10, 20, 30, 255);
        var target = BgraScaler.Downscale(source, 4, 3, 4);

        CollectionAssert.AreEqual(source, target);
    }

    private static byte[] CreateImage(
        int width,
        int height,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = a;
        }

        return pixels;
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        var index = (y * width + x) * 4;
        pixels[index] = b;
        pixels[index + 1] = g;
        pixels[index + 2] = r;
        pixels[index + 3] = a;
    }
}
