using System.Buffers.Binary;
using System.Security.Cryptography;
using CSIViewer.Protocol;

namespace CSIViewer.Security;

public sealed record SessionKeys(
    byte[] ControllerToHost,
    byte[] HostToController,
    byte[] TranscriptHash)
{
    public void Clear()
    {
        CryptographicOperations.ZeroMemory(ControllerToHost);
        CryptographicOperations.ZeroMemory(HostToController);
        CryptographicOperations.ZeroMemory(TranscriptHash);
    }
}

public sealed class SessionCrypto : IDisposable
{
    private readonly byte[] _sendKey;
    private readonly byte[] _receiveKey;
    private readonly Guid _sessionId;
    private readonly object _receiveGate = new();
    private long _sendCounter;
    private long _receiveCounter;

    public SessionCrypto(Guid sessionId, bool isController, SessionKeys keys)
    {
        _sessionId = sessionId;
        // Copy the directional keys so disposing one session object can never
        // zero memory still used by another holder of the same SessionKeys.
        _sendKey = (isController ? keys.ControllerToHost : keys.HostToController).ToArray();
        _receiveKey = (isController ? keys.HostToController : keys.ControllerToHost).ToArray();
    }

    public byte[] Encrypt(SessionFrameKind kind, ReadOnlySpan<byte> plaintext)
    {
        // Encrypt can be called concurrently (video, control, files, chat).
        // Atomic increment guarantees a unique nonce counter per frame; a
        // repeated GCM nonce would be catastrophic.
        var counter = Interlocked.Increment(ref _sendCounter) - 1;
        var nonce = new byte[SessionFrame.NonceLength];
        _sendKey.AsSpan(0, 4).CopyTo(nonce);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), counter);

        var ciphertext = new byte[plaintext.Length + SessionFrame.TagLength];
        var aad = BuildAad(kind);
        using var aes = new AesGcm(_sendKey, SessionFrame.TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length), aad);

        return SessionFrame.Create(_sessionId, kind, nonce, ciphertext);
    }

    public bool TryDecrypt(ReadOnlySpan<byte> frame, out SessionFrameKind kind, out byte[] plaintext)
    {
        kind = default;
        plaintext = [];

        if (!SessionFrame.TryReadHeader(frame, out var sessionId, out kind, out var nonce, out var ciphertext))
        {
            return false;
        }

        if (sessionId != _sessionId)
        {
            return false;
        }

        var counter = BinaryPrimitives.ReadInt64BigEndian(nonce[4..]);
        var result = new byte[ciphertext.Length - SessionFrame.TagLength];

        // Serialize the replay check and counter update. The network receive
        // loop is single-reader today, but this keeps the invariant explicit
        // if direct and relay paths ever run concurrently.
        lock (_receiveGate)
        {
            if (counter < _receiveCounter)
            {
                return false;
            }

            try
            {
                using var aes = new AesGcm(_receiveKey, SessionFrame.TagLength);
                aes.Decrypt(
                    nonce,
                    ciphertext[..^SessionFrame.TagLength],
                    ciphertext[^SessionFrame.TagLength..],
                    result,
                    BuildAad(kind));
            }
            catch (CryptographicException)
            {
                return false;
            }

            _receiveCounter = counter + 1;
        }

        plaintext = result;
        return true;
    }

    private byte[] BuildAad(SessionFrameKind kind)
    {
        var aad = new byte[17];
        _sessionId.TryWriteBytes(aad.AsSpan(0, 16));
        aad[16] = (byte)kind;
        return aad;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_sendKey);
        CryptographicOperations.ZeroMemory(_receiveKey);
    }
}
