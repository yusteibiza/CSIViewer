using CSIViewer.Networking;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class TransportTests
{
    [TestMethod]
    public async Task FrameProtocol_RoundTripsFrames()
    {
        using var stream = new MemoryStream();
        var first = new byte[] { 1, 2, 3, 4, 5 };
        var second = new byte[] { 9, 8, 7 };
        await FrameProtocol.WriteFrameAsync(stream, first);
        await FrameProtocol.WriteFrameAsync(stream, second);
        stream.Seek(0, SeekOrigin.Begin);

        CollectionAssert.AreEqual(first, await FrameProtocol.ReadFrameAsync(stream));
        CollectionAssert.AreEqual(second, await FrameProtocol.ReadFrameAsync(stream));
        Assert.IsNull(await FrameProtocol.ReadFrameAsync(stream));
    }

    [TestMethod]
    public async Task FrameProtocol_RejectsInvalidLength()
    {
        using var stream = new MemoryStream(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
        Assert.IsNull(await FrameProtocol.ReadFrameAsync(stream));
    }
}
