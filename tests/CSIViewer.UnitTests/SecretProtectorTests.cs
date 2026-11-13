using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SecretProtectorTests
{
    [TestMethod]
    public void ProtectedSecret_RoundTrips()
    {
        const string value = "SECRET-1234-ABCD";
        var protectedValue = SecretProtector.Protect(value, "test.entropy.v1");

        Assert.AreNotEqual(value, protectedValue);
        Assert.AreEqual(value, SecretProtector.Unprotect(protectedValue, "test.entropy.v1"));
    }

    [TestMethod]
    public void EmptySecret_ReturnsNull()
    {
        Assert.IsNull(SecretProtector.Unprotect(null, "entropy"));
        Assert.IsNull(SecretProtector.Unprotect("", "entropy"));
    }

    [TestMethod]
    public void TamperedSecret_ReturnsNull()
    {
        var protectedValue = SecretProtector.Protect("secret", "test.entropy.v1");
        var chars = protectedValue.ToCharArray();
        var index = chars.Length / 2;
        chars[index] = chars[index] == 'A' ? 'B' : 'A';
        var tampered = new string(chars);

        Assert.IsNull(SecretProtector.Unprotect(tampered, "test.entropy.v1"));
    }
}
