using System.Security.Cryptography;
using System.Text;

namespace CSIViewer.Security;

public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    public static string Hash(string password, out string saltBase64)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);
        saltBase64 = Convert.ToBase64String(salt);
        return Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
