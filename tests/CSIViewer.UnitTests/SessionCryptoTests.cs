using System.Buffers.Binary;
using System.Security.Cryptography;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionCryptoTests
{
    private static SessionKeys CreateKeys()
    {
        var c2h = RandomNumberGenerator.GetBytes(32);
        var h2c = RandomNumberGenerator.GetBytes(32);
        return new SessionKeys(c2h, h2c, RandomNumberGenerator.GetBytes(32));
    }

    [TestMethod]
    public async Task ParallelEncryption_NeverReusesNonces()
    {
        var sessionId = Guid.NewGuid();
        var keys = CreateKeys();
        using var crypto = new SessionCrypto(sessionId, isController: true, keys);
        const int frames = 2000;
        var encrypted = new byte[frames][];
        var nonces = new long[frames];

        Parallel.For(0, frames, index =>
        {
            encrypted[index] = crypto.Encrypt(
                SessionFrameKind.Control,
                BitConverter.GetBytes(index));
        });

        for (var i = 0; i < frames; i++)
        {
            Assert.IsTrue(SessionFrame.TryReadHeader(
                encrypted[i],
                out _,
                out _,
                out var nonce,
                out _));
            nonces[i] = BinaryPrimitives.ReadInt64BigEndian(nonce[4..]);
        }

        Assert.AreEqual(frames, nonces.Distinct().Count());
    }

    [TestMethod]
    public void ReplayedFrame_IsRejected()
    {
        var sessionId = Guid.NewGuid();
        var keys = CreateKeys();
        using var sender = new SessionCrypto(sessionId, isController: true, keys);
        using var receiver = new SessionCrypto(sessionId, isController: false, keys);
        var frame = sender.Encrypt(SessionFrameKind.Chat, "hello"u8);

        Assert.IsTrue(receiver.TryDecrypt(frame, out var kind, out var plaintext));
        Assert.AreEqual(SessionFrameKind.Chat, kind);
        CollectionAssert.AreEqual("hello"u8.ToArray(), plaintext);

        Assert.IsFalse(receiver.TryDecrypt(frame, out _, out _));
    }

    [TestMethod]
    public void OlderCounter_IsRejected()
    {
        var sessionId = Guid.NewGuid();
        var keys = CreateKeys();
        using var sender = new SessionCrypto(sessionId, isController: true, keys);
        using var receiver = new SessionCrypto(sessionId, isController: false, keys);
        var first = sender.Encrypt(SessionFrameKind.Control, "one"u8);
        var second = sender.Encrypt(SessionFrameKind.Control, "two"u8);

        Assert.IsTrue(receiver.TryDecrypt(second, out _, out _));
        Assert.IsFalse(receiver.TryDecrypt(first, out _, out _));
    }

    [TestMethod]
    public void TamperedFrame_IsRejected()
    {
        var sessionId = Guid.NewGuid();
        var keys = CreateKeys();
        using var sender = new SessionCrypto(sessionId, isController: true, keys);
        using var receiver = new SessionCrypto(sessionId, isController: false, keys);
        var frame = sender.Encrypt(SessionFrameKind.Control, "payload"u8);
        frame[^1] ^= 0xFF;

        Assert.IsFalse(receiver.TryDecrypt(frame, out _, out _));
    }

    [TestMethod]
    public void WrongSessionId_IsRejected()
    {
        var keys = CreateKeys();
        using var sender = new SessionCrypto(Guid.NewGuid(), isController: true, keys);
        using var receiver = new SessionCrypto(Guid.NewGuid(), isController: false, keys);
        var frame = sender.Encrypt(SessionFrameKind.Control, "payload"u8);

        Assert.IsFalse(receiver.TryDecrypt(frame, out _, out _));
    }
}
