using System.Text;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void Envelope_RoundTrips()
    {
        var envelope = ProtocolJson.Create("test.message", new { value = 42, name = "hola" });
        var json = ProtocolJson.Serialize(envelope);
        var parsed = ProtocolJson.Deserialize(json);
        Assert.IsNotNull(parsed);
        Assert.AreEqual("test.message", parsed.Type);
        var payload = ProtocolJson.Payload<TestPayload>(parsed);
        Assert.IsNotNull(payload);
        Assert.AreEqual(42, payload.Value);
        Assert.AreEqual("hola", payload.Name);
    }

    [TestMethod]
    public void SessionFrame_RoundTripsHeader()
    {
        var sessionId = Guid.NewGuid();
        var nonce = new byte[SessionFrame.NonceLength];
        var ciphertext = new byte[20];
        var frame = SessionFrame.Create(sessionId, SessionFrameKind.Video, nonce, ciphertext);
        Assert.IsTrue(SessionFrame.TryReadHeader(frame, out var parsedId, out var kind, out var parsedNonce, out var parsedCipher));
        Assert.AreEqual(sessionId, parsedId);
        Assert.AreEqual(SessionFrameKind.Video, kind);
        CollectionAssert.AreEqual(nonce, parsedNonce.ToArray());
        CollectionAssert.AreEqual(ciphertext, parsedCipher.ToArray());
    }

    [TestMethod]
    public void FileChunk_RoundTrips()
    {
        var transferId = Guid.NewGuid();
        var data = Encoding.UTF8.GetBytes("chunk-data");
        var payload = FileChunkPayload.Create(transferId, 2, 1024, data);
        Assert.IsTrue(FileChunkPayload.TryRead(payload, out var parsedId, out var fileIndex, out var offset, out var parsedData));
        Assert.AreEqual(transferId, parsedId);
        Assert.AreEqual(2, fileIndex);
        Assert.AreEqual(1024, offset);
        CollectionAssert.AreEqual(data, parsedData.ToArray());
    }

    [TestMethod]
    public void ControlJson_RoundTrips()
    {
        var payload = ControlJson.Create("auth.request", new AuthRequestPayload("secret", SessionPermissions.Chat));
        Assert.IsTrue(ControlJson.TryRead(payload, out var envelope));
        Assert.IsNotNull(envelope);
        Assert.AreEqual("auth.request", envelope.Type);
        var auth = ControlJson.Payload<AuthRequestPayload>(envelope);
        Assert.IsNotNull(auth);
        Assert.AreEqual("secret", auth.Password);
        Assert.AreEqual(SessionPermissions.Chat, auth.Permissions);
    }

    [TestMethod]
    public void PathValidator_RejectsTraversalAndAcceptsNestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "rd-path-tests");
        var valid = PathValidator.GetSafeDestinationPath(root, "subdir/file.txt");
        Assert.IsTrue(valid.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
        Assert.Throws<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, "../escape.txt"));
        Assert.Throws<InvalidOperationException>(() =>
            PathValidator.GetSafeDestinationPath(root, Path.Combine(Path.GetTempPath(), "absolute.txt")));
    }

    public sealed record TestPayload(int Value, string Name);
}
