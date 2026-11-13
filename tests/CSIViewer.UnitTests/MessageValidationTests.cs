using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class MessageValidationTests
{
    [TestMethod]
    public void DeviceIds_MustBeNineDigits()
    {
        Assert.IsTrue(MessageValidation.IsValidDeviceId("123456789"));
        Assert.IsFalse(MessageValidation.IsValidDeviceId("12345678"));
        Assert.IsFalse(MessageValidation.IsValidDeviceId("1234567890"));
        Assert.IsFalse(MessageValidation.IsValidDeviceId("12345abcd"));
        Assert.IsFalse(MessageValidation.IsValidDeviceId(null));
    }

    [TestMethod]
    public void NamesAndPasswords_HaveBounds()
    {
        Assert.IsTrue(MessageValidation.IsValidName("Equipo"));
        Assert.IsFalse(MessageValidation.IsValidName(""));
        Assert.IsFalse(MessageValidation.IsValidName(new string('a', 81)));
        Assert.IsFalse(MessageValidation.IsValidName("a\nb"));

        Assert.IsTrue(MessageValidation.IsValidPassword("short"));
        Assert.IsFalse(MessageValidation.IsValidPassword(""));
        Assert.IsFalse(MessageValidation.IsValidPassword(new string('a', 129)));
    }

    [TestMethod]
    public void PermissionMasks_RejectUnknownBits()
    {
        Assert.IsTrue(MessageValidation.IsValidPermissionMask(SessionPermissions.All));
        Assert.IsTrue(MessageValidation.IsValidPermissionMask(SessionPermissions.None));
        Assert.IsFalse(MessageValidation.IsValidPermissionMask((SessionPermissions)0x8000));
    }

    [TestMethod]
    public void Base64_LengthIsEnforced()
    {
        var valid = Convert.ToBase64String(new byte[32]);
        Assert.IsTrue(MessageValidation.IsValidBase64(valid, 16, 64));
        Assert.IsFalse(MessageValidation.IsValidBase64(valid, 33, 64));
        Assert.IsFalse(MessageValidation.IsValidBase64("not base64!", 1, 64));
        Assert.IsFalse(MessageValidation.IsValidBase64(null, 1, 64));
    }

    [TestMethod]
    public void Sha256_MustBeHex()
    {
        Assert.IsTrue(MessageValidation.IsValidSha256Hex(new string('a', 64)));
        Assert.IsFalse(MessageValidation.IsValidSha256Hex(new string('a', 63)));
        Assert.IsFalse(MessageValidation.IsValidSha256Hex(new string('z', 64)));
    }

    [TestMethod]
    public void InputPayloads_AreValidated()
    {
        Assert.IsTrue(MessageValidation.IsValidInput(
            new InputEventPayload("key", "keyDown", 0, 0, 0, 0, 0, 42, true)));
        Assert.IsTrue(MessageValidation.IsValidInput(
            new InputEventPayload("key", "down", 0, 0, 0, 0, 0, 42, true)));
        Assert.IsTrue(MessageValidation.IsValidInput(
            new InputEventPayload("key", "up", 0, 0, 0, 0, 0, 42, false)));
        Assert.IsTrue(MessageValidation.IsValidInput(
            new InputEventPayload("mouse", "leftDown", 0, 0, 0, 0, 0, 0, false)));
        Assert.IsTrue(MessageValidation.IsValidInput(
            new InputEventPayload(
                "mouse", "leftUp", 0, 0, 0, 0, 0, 0, false,
                HasPosition: false)));
        Assert.IsFalse(MessageValidation.IsValidInput(
            new InputEventPayload("mouse", "move", 70000, 0, 0, 0, 0, 0, false)));
        Assert.IsFalse(MessageValidation.IsValidInput(
            new InputEventPayload("key", "keyDown", 0, 0, 0, 0, 0, -1, true)));
        Assert.IsFalse(MessageValidation.IsValidInput(
            new InputEventPayload("mouse", "explode", 0, 0, 0, 0, 0, 0, false)));
        Assert.IsFalse(MessageValidation.IsValidInput(
            new InputEventPayload("unknown", "x", 0, 0, 0, 0, 0, 0, false)));
    }

    [TestMethod]
    public void VideoConfiguration_HasSafeBounds()
    {
        Assert.IsTrue(MessageValidation.IsValidVideoConfig(
            new VideoConfigPayload(1920, 1080, 30, 70)));
        Assert.IsFalse(MessageValidation.IsValidVideoConfig(
            new VideoConfigPayload(0, 1080, 30, 70)));
        Assert.IsFalse(MessageValidation.IsValidVideoConfig(
            new VideoConfigPayload(1920, 1080, 0, 70)));
        Assert.IsFalse(MessageValidation.IsValidVideoConfig(
            new VideoConfigPayload(1920, 1080, 30, 0)));
    }

    [TestMethod]
    public void MonitorLists_AreBounded()
    {
        var valid = new[]
        {
            new MonitorDescriptor(0, "Primary", 1920, 1080, true),
            new MonitorDescriptor(1, "Secondary", 1280, 1024, false)
        };
        Assert.IsTrue(MessageValidation.IsValidMonitors(valid));
        Assert.IsFalse(MessageValidation.IsValidMonitors(Array.Empty<MonitorDescriptor>()));
        Assert.IsFalse(MessageValidation.IsValidMonitors(
            [new MonitorDescriptor(-1, "bad", 1920, 1080, true)]));
    }
}
