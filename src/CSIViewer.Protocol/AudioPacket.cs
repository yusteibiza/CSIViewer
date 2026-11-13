using System.Buffers.Binary;

namespace CSIViewer.Protocol;

/// <summary>Bounded uncompressed system audio; PCM or IEEE float, never microphone data.</summary>
public static class AudioPacket
{
    public const int HeaderLength = 12;
    public const int MaxDataLength = 64 * 1024;

    public static bool IsValidFormat(int rate, int channels, int bits, bool floatingPoint) =>
        rate is >= 8000 and <= 192000 && channels is >= 1 and <= 8 &&
        (floatingPoint ? bits == 32 : bits is 16 or 24 or 32);

    public static byte[] Create(int rate, int channels, int bits, bool floatingPoint, ReadOnlySpan<byte> data)
    {
        if (!IsValidFormat(rate, channels, bits, floatingPoint) || data.Length is <= 0 or > MaxDataLength ||
            data.Length % (channels * bits / 8) != 0) throw new ArgumentException("Invalid audio packet");
        var packet = new byte[HeaderLength + data.Length];
        packet[0] = 1; // protocol version
        packet[1] = floatingPoint ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), rate);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(8), (short)channels);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(10), (short)bits);
        data.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, out int rate, out int channels, out int bits, out bool floatingPoint)
    {
        rate = channels = bits = 0;
        floatingPoint = false;
        if (packet.Length <= HeaderLength || packet.Length > HeaderLength + MaxDataLength ||
            packet[0] != 1 || packet[1] > 1 || packet[2] != 0 || packet[3] != 0) return false;
        floatingPoint = packet[1] == 1;
        rate = BinaryPrimitives.ReadInt32LittleEndian(packet[4..]);
        channels = BinaryPrimitives.ReadInt16LittleEndian(packet[8..]);
        bits = BinaryPrimitives.ReadInt16LittleEndian(packet[10..]);
        return IsValidFormat(rate, channels, bits, floatingPoint) &&
            (packet.Length - HeaderLength) % (channels * bits / 8) == 0;
    }
}
