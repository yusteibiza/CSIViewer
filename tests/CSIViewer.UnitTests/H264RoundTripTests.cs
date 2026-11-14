using CSIViewer.Media;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class H264RoundTripTests
{
    [TestMethod]
    [DataRow(640, 360)]
    [DataRow(1920, 1080)]
    [DataRow(2560, 1440)]
    public async Task PaddedDesktop_DecodesWithoutGreenBandOrLongStartup(int width, int height)
    {
        using var encoder = new H264Encoder(width, height, 20, 12_000_000);
        using var decoder = new H264Decoder(width, height, 20, 90);
        var source = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i + 2] = 255;
            source[i + 3] = 255;
        }
        var first = -1;
        var outputs = 0;
        byte[]? retained = null;
        byte[]? retainedSnapshot = null;
        for (var n = 0; n < 30; n++)
        {
            var packet = encoder.Encode(source);
            if (packet.Length == 0) continue;
            foreach (var frame in await decoder.DecodeAsync(packet))
            {
                if (first < 0) first = n;
                outputs++;
                if (retained is null) { retained = frame.Data; retainedSnapshot = frame.Data.ToArray(); }
                else Assert.AreNotSame(retained, frame.Data);
                foreach (var y in new[] { 0, 15, height - 1 })
                {
                    var i = (y * width + width / 2) * 4;
                    Assert.IsLessThanOrEqualTo((byte)8, frame.Data[i]);
                    Assert.IsLessThanOrEqualTo((byte)8, frame.Data[i + 1]);
                    Assert.IsGreaterThanOrEqualTo((byte)245, frame.Data[i + 2]);
                }
            }
        }
        Assert.IsTrue(first is >= 0 and < 10, $"First output was input {first}.");
        Assert.IsTrue(outputs >= 20, $"Only {outputs}/30 frames decoded.");
        Assert.IsTrue(decoder.OutputBufferAllocations <= 3, $"Output sample allocated {decoder.OutputBufferAllocations} times.");
        CollectionAssert.AreEqual(retainedSnapshot!, retained!);
    }
}
