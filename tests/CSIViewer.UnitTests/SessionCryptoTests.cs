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
    public void DisposedReceiver_RejectsFramesUsingClearedKey()
    {
        var sessionId = Guid.NewGuid();
        using var receiver = new SessionCrypto(sessionId, isController: false, CreateKeys());
        receiver.Dispose();
        using var sender = new SessionCrypto(sessionId, isController: true,
            new SessionKeys(new byte[32], new byte[32], new byte[32]));
        var forged = sender.Encrypt(SessionFrameKind.Input, "delayed"u8);

        Assert.IsFalse(receiver.TryDecrypt(forged, out _, out var plaintext));
        Assert.IsEmpty(plaintext);
    }

    [TestMethod]
    public void DisposedSender_RejectsEncryption()
    {
        using var sender = new SessionCrypto(Guid.NewGuid(), isController: true, CreateKeys());
        sender.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            sender.Encrypt(SessionFrameKind.Chat, "late"u8));
    }

    [TestMethod]
    public async Task OrderedSend_PreventsTransportOvertakingAcrossFrameKinds()
    {
        var sessionId = Guid.NewGuid();
        var keys = CreateKeys();
        using var sender = new SessionCrypto(sessionId, true, keys);
        using var receiver = new SessionCrypto(sessionId, false, keys);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new List<byte[]>();
        var secondEntered = false;
        var first = sender.EncryptAndSendAsync(SessionFrameKind.Video, new byte[] { 1 }, async (frame, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            frames.Add(frame);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = sender.EncryptAndSendAsync(SessionFrameKind.Input, new byte[] { 2 }, (frame, _) =>
        {
            secondEntered = true;
            frames.Add(frame);
            return Task.CompletedTask;
        });
        try
        {
            Assert.IsFalse(secondEntered, "A later nonce must not overtake a blocked transport submission.");
            Assert.IsFalse(second.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.HasCount(2, frames);
        Assert.IsTrue(receiver.TryDecrypt(frames[0], out var firstKind, out _));
        Assert.IsTrue(receiver.TryDecrypt(frames[1], out var secondKind, out _));
        Assert.AreEqual(SessionFrameKind.Video, firstKind);
        Assert.AreEqual(SessionFrameKind.Input, secondKind);
    }

    [TestMethod]
    public async Task OrderedSend_CancellationAndTransportFailureReleaseQueue()
    {
        using var sender = new SessionCrypto(Guid.NewGuid(), true, CreateKeys());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = sender.EncryptAndSendAsync(SessionFrameKind.Chat, new byte[] { 1 }, async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            throw new IOException("synthetic transport failure");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        var canceled = sender.EncryptAndSendAsync(SessionFrameKind.Chat, new byte[] { 2 },
            (_, _) => throw new AssertFailedException("Canceled queued frame must not reach transport."), cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceled);
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsExactlyAsync<IOException>(() => first);
        await sender.EncryptAndSendAsync(SessionFrameKind.Chat, new byte[] { 3 }, (_, _) => Task.CompletedTask)
            .WaitAsync(TimeSpan.FromSeconds(3));
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
