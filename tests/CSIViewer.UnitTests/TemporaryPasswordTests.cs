using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class TemporaryPasswordTests
{
    [TestMethod]
    public void GeneratedPassword_HasExpectedShape()
    {
        var password = TemporaryPasswordGenerator.Generate();

        Assert.AreEqual(12, password.Raw.Length);
        Assert.AreEqual(14, password.Display.Length);
        Assert.AreEqual(password.Raw[..4], password.Display[..4]);
        Assert.AreEqual('-', password.Display[4]);
        Assert.AreEqual(password.Raw[4..8], password.Display[5..9]);
        Assert.AreEqual('-', password.Display[9]);
        Assert.AreEqual(password.Raw[8..12], password.Display[10..14]);
        Assert.IsFalse(password.Raw.Any(c => "01OIL".Contains(c)));
    }

    [TestMethod]
    public void GeneratedPasswords_AreNotRepeated()
    {
        var values = Enumerable.Range(0, 100)
            .Select(_ => TemporaryPasswordGenerator.Generate().Raw)
            .ToHashSet();

        Assert.AreEqual(100, values.Count);
    }

    [TestMethod]
    public void GeneratedToken_IsUrlSafeAndLongEnough()
    {
        var token = TemporaryPasswordGenerator.GenerateToken();

        Assert.IsTrue(token.Length >= 32);
        Assert.IsFalse(token.Contains('+'));
        Assert.IsFalse(token.Contains('/'));
        Assert.IsFalse(token.Contains('='));
    }

    [TestMethod]
    public void DisplayedPassword_NormalizesToStoredRawValue()
    {
        var password = TemporaryPasswordGenerator.Generate();

        Assert.AreEqual(password.Raw, TemporaryPasswordGenerator.NormalizeInput(password.Display));
        Assert.AreEqual(
            password.Raw,
            TemporaryPasswordGenerator.NormalizeInput("  " + password.Display.ToLowerInvariant() + " "));
    }

    [TestMethod]
    public void HashOfRawPassword_VerifiesTheTypedDisplayedPassword()
    {
        var password = TemporaryPasswordGenerator.Generate();
        var hash = PasswordHasher.Hash(password.Raw, out var salt);

        Assert.IsTrue(PasswordHasher.Verify(
            TemporaryPasswordGenerator.NormalizeInput(password.Display),
            salt,
            hash));
        Assert.IsFalse(PasswordHasher.Verify(
            TemporaryPasswordGenerator.NormalizeInput("XXXX-XXXX-XXXX"),
            salt,
            hash));
    }
}
