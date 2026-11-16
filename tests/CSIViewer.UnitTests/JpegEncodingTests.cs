using System.Reflection;
using CSIViewer.Media;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class JpegEncodingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QualityActuallyControlsCompression(bool gdiPath)
    {
        var pixels = new byte[128 * 128 * 4];
        new Random(42).NextBytes(pixels);
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        async Task<byte[]> Encode(int quality) => gdiPath
            ? await (Task<byte[]>)typeof(GdiScreenCapture).GetMethod("EncodeJpegAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [pixels, 128, 128, quality])!
            : await JpegEncoder.EncodeBgraAsync(pixels, 128, 128, quality, 2560);
        var low = await Encode(20);
        var high = await Encode(95);
        Assert.IsTrue(high.Length > low.Length, "JPEG quality must configure compression, not merely be accepted as metadata.");
    }

    [TestMethod]
    [DataRow(false, 20)]
    [DataRow(false, 62)]
    [DataRow(false, 95)]
    [DataRow(true, 20)]
    [DataRow(true, 62)]
    [DataRow(true, 95)]
    public async Task SyntheticBgraEncodesAndDecodesWithQualityOption(bool gdiPath, int quality)
    {
        var pixels = Pixels(64, 48);
        var jpeg = gdiPath
            ? await (Task<byte[]>)typeof(GdiScreenCapture).GetMethod("EncodeJpegAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [pixels, 64, 48, quality])!
            : await JpegEncoder.EncodeBgraAsync(pixels, 64, 48, quality, 2560);
        await VerifyJpegAsync(jpeg, 64, 48);
    }

    [TestMethod]
    public async Task SyntheticJpegScalingAndQualityClampingRemainSupported()
    {
        foreach (var quality in new[] { -100, 1000 })
        {
            var jpeg = await JpegEncoder.EncodeBgraAsync(Pixels(1280, 720), 1280, 720, quality, 640);
            await VerifyJpegAsync(jpeg, 640, 360);
        }
    }

    [TestMethod]
    public async Task RepeatedIndependentJpegFramesDoNotRequireCaptureOrUi()
    {
        for (var index = 0; index < 32; index++)
        {
            var jpeg = await JpegEncoder.EncodeBgraAsync(Pixels(64, 48, index), 64, 48, 95, 2560);
            await VerifyJpegAsync(jpeg, 64, 48);
        }
    }

    private static async Task VerifyJpegAsync(byte[] jpeg, int width, int height)
    {
        Assert.IsTrue(jpeg.Length > 4);
        Assert.AreEqual((byte)0xFF, jpeg[0]);
        Assert.AreEqual((byte)0xD8, jpeg[1]);
        var decoded = await JpegDecoder.DecodeToBgraAsync(jpeg);
        Assert.AreEqual(width, decoded.Width);
        Assert.AreEqual(height, decoded.Height);
        Assert.AreEqual(width * height * 4, decoded.Bgra.Length);
    }

    private static byte[] Pixels(int width, int height, int offset = 0)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)((index / 4 + offset) % 256);
            pixels[index + 1] = (byte)((index / (width * 4) + offset) % 256);
            pixels[index + 2] = 80;
            pixels[index + 3] = 255;
        }
        return pixels;
    }
}
