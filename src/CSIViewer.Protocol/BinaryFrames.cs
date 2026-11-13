using System.Buffers.Binary;
using CSIViewer.Core;

namespace CSIViewer.Protocol;

public enum SessionFrameKind : byte
{
    Control = 1,
    Video = 2,
    FileChunk = 3,
    Chat = 4,
    Input = 5,
    Audio = 6,
    Clipboard = 7
}

public static class SessionFrame
{
    public const int SessionIdLength = 16;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int HeaderLength = SessionIdLength + 1;

    public static byte[] Create(Guid sessionId, SessionFrameKind kind, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext)
    {
        var frame = new byte[HeaderLength + nonce.Length + ciphertext.Length];
        sessionId.TryWriteBytes(frame.AsSpan(0, SessionIdLength));
        frame[SessionIdLength] = (byte)kind;
        nonce.CopyTo(frame.AsSpan(HeaderLength));
        ciphertext.CopyTo(frame.AsSpan(HeaderLength + nonce.Length));
        return frame;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out Guid sessionId, out SessionFrameKind kind, out ReadOnlySpan<byte> nonce, out ReadOnlySpan<byte> ciphertext)
    {
        sessionId = Guid.Empty;
        kind = default;
        nonce = default;
        ciphertext = default;

        if (frame.Length < HeaderLength + NonceLength + TagLength)
        {
            return false;
        }

        sessionId = new Guid(frame[..SessionIdLength]);
        kind = (SessionFrameKind)frame[SessionIdLength];
        nonce = frame.Slice(HeaderLength, NonceLength);
        ciphertext = frame[(HeaderLength + NonceLength)..];
        return true;
    }
}

public sealed record AuthRequestPayload(string Password, SessionPermissions Permissions);
public sealed record AuthResultPayload(bool Accepted, SessionPermissions GrantedPermissions, string? Reason = null);
public sealed record VideoConfigPayload(int Width, int Height, int Fps, int Quality);
public sealed record MonitorDescriptor(int Index, string Name, int Width, int Height, bool IsPrimary)
{
    public override string ToString() => $"{Name} · {Width}x{Height}{(IsPrimary ? " · principal" : "")}";
}
public sealed record MonitorsPayload(IReadOnlyList<MonitorDescriptor> Monitors, int ActiveIndex = 0);
public sealed record SelectMonitorPayload(int Index);
public sealed record ChatPayload(string Id, string Text, long SentUnixMs);
public sealed record InputEventPayload(
    string Kind,
    string Action,
    int X,
    int Y,
    int Button,
    int Delta,
    int KeyCode,
    int ScanCode,
    bool Down,
    int MonitorIndex = 0,
    bool HasPosition = true,
    bool Extended = false,
    string? TraceId = null,
    bool TraceSampled = false);

public sealed record PermissionUpdatePayload(SessionPermissions Permissions);

public sealed record FileOfferPayload(
    string TransferId,
    int FileIndex,
    string FileName,
    long Size,
    string Sha256,
    string RelativePath,
    string? DestinationToken = null);

public sealed record FileAcceptPayload(string TransferId, bool Accepted, long ResumeOffset = 0, string? Reason = null);
public sealed record FileResultPayload(string TransferId, bool Success, string? Path, string? Error = null);
public sealed record FileCancelPayload(string TransferId, string? Reason = null);
public sealed record FilePausePayload(string TransferId, bool Paused);
public sealed record FileDirectoryPayload(string RelativePath, string? DestinationToken = null, string? RequestId = null);

public static class FileChunkPayload
{
    public const int HeaderSize = 16 + 4 + 8;

    public static byte[] Create(Guid transferId, int fileIndex, long offset, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[HeaderSize + data.Length];
        transferId.TryWriteBytes(buffer.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), fileIndex);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(20, 8), offset);
        data.CopyTo(buffer.AsSpan(HeaderSize));
        return buffer;
    }

    public static bool TryRead(ReadOnlySpan<byte> payload, out Guid transferId, out int fileIndex, out long offset, out ReadOnlySpan<byte> data)
    {
        transferId = Guid.Empty;
        fileIndex = -1;
        offset = -1;
        data = default;

        if (payload.Length < HeaderSize)
        {
            return false;
        }

        transferId = new Guid(payload[..16]);
        fileIndex = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16, 4));
        offset = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(20, 8));
        data = payload[HeaderSize..];
        return fileIndex >= 0 && offset >= 0;
    }
}
