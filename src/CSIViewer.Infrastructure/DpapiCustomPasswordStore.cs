using System.Security.Cryptography;
using System.Text;
using CSIViewer.Application;

namespace CSIViewer.Infrastructure;

public sealed class DpapiCustomPasswordStore : ICustomPasswordStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CSIViewer.CustomPassword.v1");
    private static string Path => System.IO.Path.Combine(AppPaths.DataDirectory, "custom-password.bin");

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(Path, cancellationToken).ConfigureAwait(false);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch
            {
                // Best effort.
            }

            return;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var temp = Path + ".tmp";
            await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
