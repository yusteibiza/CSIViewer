using System.Text;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SecurityTests
{
    [TestMethod]
    public void PasswordHasher_AcceptsCorrectPassword_AndRejectsWrongPassword()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple", out var salt);
        Assert.IsTrue(PasswordHasher.Verify("correct horse battery staple", salt, hash));
        Assert.IsFalse(PasswordHasher.Verify("wrong password", salt, hash));
        Assert.IsFalse(PasswordHasher.Verify("correct horse battery staple", "invalid", hash));
    }

    [TestMethod]
    public void PasswordHasher_UsesRandomSalt()
    {
        var first = PasswordHasher.Hash("same-password", out var firstSalt);
        var second = PasswordHasher.Hash("same-password", out var secondSalt);
        Assert.AreNotEqual(firstSalt, secondSalt);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void TemporaryPassword_HasExpectedShape()
    {
        var password = TemporaryPasswordGenerator.Generate();
        Assert.AreEqual(12, password.Raw.Length);
        StringAssert.Matches(password.Raw, new System.Text.RegularExpressions.Regex("^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{12}$"));
        Assert.AreEqual("ABCD-EFGH-JKLM".Length, password.Display.Length);
    }

    [TestMethod]
    public void DeviceIdentity_SignsAndVerifies_AndRejectsTampering()
    {
        using var identity = DeviceIdentity.CreateNew();
        var data = Encoding.UTF8.GetBytes("csiviewer-test");
        var signature = identity.Sign(data);
        Assert.IsTrue(DeviceIdentity.Verify(identity.PublicKeyBase64, data, signature));

        data[0] ^= 1;
        Assert.IsFalse(DeviceIdentity.Verify(identity.PublicKeyBase64, data, signature));
    }

    [TestMethod]
    public void SessionHandshake_DerivesMatchingKeys_AndCryptoWorksBothWays()
    {
        using var controller = new SessionHandshake();
        using var host = new SessionHandshake();
        var sessionId = Guid.NewGuid();
        var transcript = SessionHandshake.BuildFinalTranscript(
            sessionId.ToString(),
            "111111111",
            "222222222",
            controller.PublicKeyBase64,
            host.PublicKeyBase64,
            controller.Nonce,
            host.Nonce,
            (int)CSIViewer.Core.SessionPermissions.Chat);

        var controllerKeys = controller.DeriveKeys(host.PublicKeyBase64, transcript);
        var hostKeys = host.DeriveKeys(controller.PublicKeyBase64, transcript);
        CollectionAssert.AreEqual(controllerKeys.ControllerToHost, hostKeys.ControllerToHost);
        CollectionAssert.AreEqual(controllerKeys.HostToController, hostKeys.HostToController);

        using var controllerCrypto = new SessionCrypto(sessionId, isController: true, controllerKeys);
        using var hostCrypto = new SessionCrypto(sessionId, isController: false, hostKeys);

        var payload = Encoding.UTF8.GetBytes("hello host");
        var frame = controllerCrypto.Encrypt(SessionFrameKind.Chat, payload);
        Assert.IsTrue(hostCrypto.TryDecrypt(frame, out var kind, out var plaintext));
        Assert.AreEqual(SessionFrameKind.Chat, kind);
        CollectionAssert.AreEqual(payload, plaintext);

        var reply = hostCrypto.Encrypt(SessionFrameKind.Control, Encoding.UTF8.GetBytes("hello controller"));
        Assert.IsTrue(controllerCrypto.TryDecrypt(reply, out _, out var replyPlain));
        Assert.AreEqual("hello controller", Encoding.UTF8.GetString(replyPlain));
    }

    [TestMethod]
    public void SessionCrypto_RejectsTamperingAndReplay()
    {
        using var identity = DeviceIdentity.CreateNew();
        _ = identity;
        using var controller = new SessionHandshake();
        using var host = new SessionHandshake();
        var sessionId = Guid.NewGuid();
        var transcript = SessionHandshake.BuildFinalTranscript(
            sessionId.ToString(), "1", "2", controller.PublicKeyBase64, host.PublicKeyBase64,
            controller.Nonce, host.Nonce, 1);
        var keys = controller.DeriveKeys(host.PublicKeyBase64, transcript);
        using var crypto = new SessionCrypto(sessionId, true, keys);

        var frame = crypto.Encrypt(SessionFrameKind.Chat, Encoding.UTF8.GetBytes("message"));
        var tampered = frame.ToArray();
        tampered[^1] ^= 0xFF;
        Assert.IsFalse(crypto.TryDecrypt(tampered, out _, out _));

        using var receiver = new SessionCrypto(sessionId, false, host.DeriveKeys(controller.PublicKeyBase64, transcript));
        Assert.IsTrue(receiver.TryDecrypt(frame, out _, out _));
        Assert.IsFalse(receiver.TryDecrypt(frame, out _, out _));
    }
}
