using CSIViewer.Media;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class Nv12LayoutTests
{
    [TestMethod]
    [DataRow(640, 360, 640, 368)]
    [DataRow(1920, 1080, 1920, 1088)]
    [DataRow(1280, 720, 1280, 720)]
    [DataRow(640, 360, 672, 368)]
    public void PaddedLumaPlane_UsesCodedHeightForChroma(int width, int height, int stride, int codedHeight)
    {
        var data = new byte[stride * codedHeight * 3 / 2];
        for (var y = 0; y < height; y++)
        {
            Array.Fill(data, (byte)81, y * stride, width);
        }
        for (var y = 0; y < codedHeight / 2; y++)
        for (var x = 0; x < width; x += 2)
        {
            var uv = stride * codedHeight + y * stride + x;
            data[uv] = 90;
            data[uv + 1] = 240;
        }
        var pixels = H264Decoder.Nv12ToBgra(data, width, height, stride, codedHeight);
        Assert.HasCount(width * height * 4, pixels);
        foreach (var y in new[] { 0, 15, height - 1 })
        {
            var index = (y * width + width / 2) * 4;
            Assert.IsLessThanOrEqualTo((byte)3, pixels[index]);
            Assert.IsLessThanOrEqualTo((byte)3, pixels[index + 1]);
            Assert.IsGreaterThanOrEqualTo((byte)250, pixels[index + 2]);
        }
    }

    [TestMethod]
    public void TruncatedNv12_IsRejectedBeforeConversion()
    {
        Assert.Throws<ArgumentException>(() => H264Decoder.Nv12ToBgra(new byte[100], 640, 360, 640, 368));
    }
}
