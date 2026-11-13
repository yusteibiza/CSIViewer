using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSIViewer.Application;

namespace CSIViewer.Infrastructure;

public sealed class DpapiIdentityStore : IIdentityStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CSIViewer.Identity.v1");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class StoredIdentityFile
    {
        public string? DeviceId { get; set; }
        public string DeviceName { get; set; } = Environment.MachineName;
        public string PublicKey { get; set; } = "";
        public string ProtectedPrivateKey { get; set; } = "";
    }

    public async Task<StoredIdentity?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.IdentityPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.IdentityPath);
            var file = await JsonSerializer.DeserializeAsync<StoredIdentityFile>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            if (file is null || string.IsNullOrWhiteSpace(file.ProtectedPrivateKey))
            {
                return null;
            }

            var protectedBytes = Convert.FromBase64String(file.ProtectedPrivateKey);
            var privateKey = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return new StoredIdentity(file.DeviceId, file.DeviceName, privateKey, file.PublicKey);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
        {
            return null;
        }
    }

    public async Task SaveAsync(StoredIdentity identity, CancellationToken cancellationToken = default)
    {
        var protectedKey = ProtectedData.Protect(identity.PrivateKeyPkcs8, Entropy, DataProtectionScope.CurrentUser);
        var file = new StoredIdentityFile
        {
            DeviceId = identity.DeviceId,
            DeviceName = identity.DeviceName,
            PublicKey = identity.PublicKeyBase64,
            ProtectedPrivateKey = Convert.ToBase64String(protectedKey)
        };

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temp = AppPaths.IdentityPath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, file, Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, AppPaths.IdentityPath, overwrite: true);
    }
}
