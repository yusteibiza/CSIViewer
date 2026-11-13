using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionHandshakeTests
{
    [TestMethod]
    public void BothSides_DeriveTheSameDirectionalKeys()
    {
        using var controller = new SessionHandshake();
        using var host = new SessionHandshake();
        var sessionId = Guid.NewGuid().ToString("D");
        const string controllerId = "123456789";
        const string hostId = "987654321";
        const int permissions = 7;

        var clientTranscript = SessionHandshake.BuildClientTranscript(
            sessionId,
            controllerId,
            hostId,
            controller.PublicKeyBase64,
            controller.Nonce,
            permissions);
        var finalTranscript = SessionHandshake.BuildFinalTranscript(
            sessionId,
            controllerId,
            hostId,
            controller.PublicKeyBase64,
            host.PublicKeyBase64,
            controller.Nonce,
            host.Nonce,
            permissions);

        using var controllerKeysCrypto = new SessionCrypto(
            Guid.Parse(sessionId),
            isController: true,
            controller.DeriveKeys(host.PublicKeyBase64, finalTranscript));
        using var hostKeysCrypto = new SessionCrypto(
            Guid.Parse(sessionId),
            isController: false,
            host.DeriveKeys(controller.PublicKeyBase64, finalTranscript));

        var frame = controllerKeysCrypto.Encrypt(SessionFrameKind.Control, clientTranscript);
        Assert.IsTrue(hostKeysCrypto.TryDecrypt(frame, out _, out var decrypted));
        CollectionAssert.AreEqual(clientTranscript, decrypted);

        var response = hostKeysCrypto.Encrypt(SessionFrameKind.Control, finalTranscript);
        Assert.IsTrue(controllerKeysCrypto.TryDecrypt(response, out _, out var responsePlaintext));
        CollectionAssert.AreEqual(finalTranscript, responsePlaintext);
    }

    [TestMethod]
    public void DifferentNonces_ProduceDifferentTranscripts()
    {
        using var first = new SessionHandshake();
        using var second = new SessionHandshake();

        Assert.AreNotEqual(first.Nonce, second.Nonce);
        Assert.AreNotEqual(first.PublicKeyBase64, second.PublicKeyBase64);
    }
}
