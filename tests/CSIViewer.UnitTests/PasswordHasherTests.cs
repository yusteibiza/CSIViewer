using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class PasswordHasherTests
{
    [TestMethod]
    public void CorrectPassword_Verifies()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple", out var salt);

        Assert.IsTrue(PasswordHasher.Verify("correct horse battery staple", salt, hash));
        Assert.IsFalse(PasswordHasher.Verify("wrong password", salt, hash));
    }

    [TestMethod]
    public void SaltIsRandomPerHash()
    {
        var first = PasswordHasher.Hash("same-password", out var firstSalt);
        var second = PasswordHasher.Hash("same-password", out var secondSalt);

        Assert.AreNotEqual(firstSalt, secondSalt);
        Assert.AreNotEqual(first, second);
        Assert.IsTrue(PasswordHasher.Verify("same-password", firstSalt, first));
        Assert.IsTrue(PasswordHasher.Verify("same-password", secondSalt, second));
    }

    [TestMethod]
    public void MalformedValues_AreRejected()
    {
        Assert.IsFalse(PasswordHasher.Verify("", "salt", "hash"));
        Assert.IsFalse(PasswordHasher.Verify("password", "", "hash"));
        Assert.IsFalse(PasswordHasher.Verify("password", "not-base64!", "hash"));
        Assert.IsFalse(PasswordHasher.Verify("password", "c2FsdA==", "not-base64!"));
    }
}
