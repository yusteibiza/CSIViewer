using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class AudioPacketTests
{
    [TestMethod]
    [DataRow(48000, 2, 32, true)]
    [DataRow(44100, 2, 16, false)]
    [DataRow(48000, 6, 24, false)]
    public void AudioRoundTripsBoundedFormatAndSamples(int rate, int channels, int bits, bool floating)
    {
        var data = new byte[channels * bits / 8 * 100];
        Random.Shared.NextBytes(data);
        var packet = AudioPacket.Create(rate, channels, bits, floating, data);
        Assert.IsTrue(AudioPacket.TryRead(packet, out var actualRate, out var actualChannels, out var actualBits, out var actualFloat));
        Assert.AreEqual(rate, actualRate);
        Assert.AreEqual(channels, actualChannels);
        Assert.AreEqual(bits, actualBits);
        Assert.AreEqual(floating, actualFloat);
        CollectionAssert.AreEqual(data, packet[AudioPacket.HeaderLength..]);
    }

    [TestMethod]
    public void RejectsTruncatedOversizedUnknownAndMisalignedAudio()
    {
        var good = AudioPacket.Create(48000, 2, 32, true, new byte[16]);
        Assert.IsFalse(AudioPacket.TryRead(good.AsSpan(0, good.Length - 1), out _, out _, out _, out _));
        Assert.IsFalse(AudioPacket.TryRead(new byte[AudioPacket.MaxDataLength + AudioPacket.HeaderLength + 1], out _, out _, out _, out _));
        Assert.IsFalse(AudioPacket.TryRead(good.AsSpan(0, 10), out _, out _, out _, out _));
        good[0] = 2;
        Assert.IsFalse(AudioPacket.TryRead(good, out _, out _, out _, out _));
        good[0] = 1; good[1] = 7;
        Assert.IsFalse(AudioPacket.TryRead(good, out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow(0, 2, 32, true)]
    [DataRow(48000, 0, 32, true)]
    [DataRow(48000, 9, 32, true)]
    [DataRow(48000, 2, 16, true)]
    [DataRow(48000, 2, 8, false)]
    public void RejectsUnsafeAudioFormats(int rate, int channels, int bits, bool floating) =>
        Assert.IsFalse(AudioPacket.IsValidFormat(rate, channels, bits, floating));

    [TestMethod]
    public void NewPermissionsDoNotImplicitlyFollowScreenControlOrChat()
    {
        var legacy = SessionPermissions.ViewScreen | SessionPermissions.ControlInput | SessionPermissions.Chat |
            SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles;
        Assert.IsFalse(legacy.HasFlag(SessionPermissions.Audio));
        Assert.IsFalse(legacy.HasFlag(SessionPermissions.Clipboard));
        Assert.AreEqual(SessionPermissions.Audio | SessionPermissions.Clipboard,
            SessionPermissionPolicy.Sanitize(SessionPermissions.Audio | SessionPermissions.Clipboard | (SessionPermissions)0x4000));
        Assert.IsTrue(MessageValidation.IsValidPermissionMask(SessionPermissions.Audio | SessionPermissions.Clipboard));
    }
}
