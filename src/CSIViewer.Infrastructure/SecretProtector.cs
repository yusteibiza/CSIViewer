using System.Security.Cryptography;
using System.Text;

namespace CSIViewer.Infrastructure;

/// <summary>
/// Protects small secrets at rest with Windows DPAPI scoped to the current
/// user. Used for secrets that the application must reuse, not for password
/// verification (passwords are stored as PBKDF2 hashes).
/// </summary>
public static class SecretProtector
{
    public static string Protect(string value, string entropy)
    {
        var plaintext = Encoding.UTF8.GetBytes(value ?? "");
        try
        {
            var protectedBytes = ProtectedData.Protect(
                plaintext,
                Encoding.UTF8.GetBytes(entropy),
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string? Unprotect(string? protectedValue, string entropy)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Encoding.UTF8.GetBytes(entropy),
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
