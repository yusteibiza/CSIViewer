using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using CSIViewer.Media;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SystemDesktopGuardTests
{
    [TestMethod]
    public void NormalUserCannotEnableTheSystemDesktopBackend()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem && Process.GetCurrentProcess().SessionId > 0)
        {
            Assert.Inconclusive("This guard test must run outside a privileged interactive worker.");
            return;
        }
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new SystemDesktopInput());
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new SystemDesktopCapture());
    }

    [TestMethod]
    public void SpanProvisionedVerifierIsCompatibleWithTheExistingEncryptedSessionAuthentication()
    {
        const string synthetic = "Synthetic-login-lab-1234";
        var salt = RandomNumberGenerator.GetBytes(PasswordHasher.SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(synthetic.AsSpan(), salt, PasswordHasher.Iterations, HashAlgorithmName.SHA256, PasswordHasher.HashSize);
        try
        {
            Assert.IsTrue(PasswordHasher.Verify(synthetic, Convert.ToBase64String(salt), Convert.ToBase64String(hash)));
            Assert.IsFalse(PasswordHasher.Verify("wrong-password", Convert.ToBase64String(salt), Convert.ToBase64String(hash)));
        }
        finally { CryptographicOperations.ZeroMemory(hash); CryptographicOperations.ZeroMemory(salt); }
    }
}
