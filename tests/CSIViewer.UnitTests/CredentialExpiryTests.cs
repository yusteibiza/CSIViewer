using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class CredentialExpiryTests
{
    [TestMethod]
    public void ZeroLifetime_MeansNoExpiry()
    {
        Assert.IsNull(CredentialExpiry.Calculate(DateTimeOffset.UtcNow, 0));
    }

    [TestMethod]
    public void Lifetime_ProducesExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var expires = CredentialExpiry.Calculate(now, 60);

        Assert.IsNotNull(expires);
        Assert.AreEqual(now.AddMinutes(60), expires.Value);
        Assert.IsFalse(CredentialExpiry.IsExpired(expires, now.AddMinutes(59)));
        Assert.IsTrue(CredentialExpiry.IsExpired(expires, now.AddMinutes(60)));
    }

    [TestMethod]
    public void NullExpiry_IsNeverExpired()
    {
        Assert.IsFalse(CredentialExpiry.IsExpired(null, DateTimeOffset.UtcNow.AddYears(100)));
    }
}
