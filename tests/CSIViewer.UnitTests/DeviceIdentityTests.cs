using System.Text;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class DeviceIdentityTests
{
    [TestMethod]
    public void Signature_RoundTrips()
    {
        using var identity = DeviceIdentity.CreateNew();
        var data = Encoding.UTF8.GetBytes("RD-AUTH-v1|nonce|device");

        var signature = identity.Sign(data);

        Assert.IsTrue(DeviceIdentity.Verify(identity.PublicKeyBase64, data, signature));
        Assert.IsFalse(DeviceIdentity.Verify(identity.PublicKeyBase64, "other"u8, signature));
    }

    [TestMethod]
    public void OtherKey_CannotVerify()
    {
        using var signer = DeviceIdentity.CreateNew();
        using var other = DeviceIdentity.CreateNew();
        var data = "payload"u8.ToArray();

        var signature = signer.Sign(data);

        Assert.IsFalse(DeviceIdentity.Verify(other.PublicKeyBase64, data, signature));
    }

    [TestMethod]
    public void TamperedSignature_IsRejected()
    {
        using var identity = DeviceIdentity.CreateNew();
        var data = "payload"u8.ToArray();
        var signature = identity.Sign(data);
        signature[^1] ^= 0xFF;

        Assert.IsFalse(DeviceIdentity.Verify(identity.PublicKeyBase64, data, signature));
    }
}
