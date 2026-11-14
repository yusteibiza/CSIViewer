using CSIViewer.Media;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class Nv12PixelConverterTests
{
    [TestMethod]
    [DataRow(2, 2, 2, 2)]
    [DataRow(6, 4, 9, 16)]
    [DataRow(8, 4, 8, 4)]
    [DataRow(10, 6, 16, 16)]
    [DataRow(34, 34, 40, 48)]
    [DataRow(640, 360, 672, 368)]
    public void SimdAndScalarMatchAndStayWithinOneLevelOfOriginal(int width, int height, int stride, int planeHeight)
    {
        var source = new byte[stride * (planeHeight + height / 2)];
        new Random(42).NextBytes(source);
        var scalar = Nv12PixelConverter.Convert(source, width, height, stride, planeHeight, useSimd: false);
        var vector = Nv12PixelConverter.Convert(source, width, height, stride, planeHeight);
        CollectionAssert.AreEqual(scalar, vector);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var uv = stride * planeHeight + y / 2 * stride + x / 2 * 2;
            var c = 1.164 * (source[y * stride + x] - 16);
            var u = source[uv] - 128;
            var v = source[uv + 1] - 128;
            var target = (y * width + x) * 4;
            Near(c + 2.017 * u, vector[target]);
            Near(c - .392 * u - .813 * v, vector[target + 1]);
            Near(c + 1.596 * v, vector[target + 2]);
            Assert.AreEqual((byte)255, vector[target + 3]);
        }
        static void Near(double expected, byte actual) =>
            Assert.IsTrue(Math.Abs(Math.Clamp((int)expected, 0, 255) - actual) <= 1);
    }

    [TestMethod]
    public void InvalidLayoutsAreRejectedBeforeUnsafeLoads()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Nv12PixelConverter.Convert(new byte[11], 4, 2, 4));
        Assert.ThrowsExactly<ArgumentException>(() => Nv12PixelConverter.Convert(new byte[20], 3, 2, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Nv12PixelConverter.Convert(new byte[20], 4, 4, 4, 2));
        Assert.ThrowsExactly<OverflowException>(() => Nv12PixelConverter.Convert([], int.MaxValue - 1, 4, int.MaxValue - 1));
    }
}
