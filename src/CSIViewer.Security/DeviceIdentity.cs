using System.Security.Cryptography;

namespace CSIViewer.Security;

public sealed class DeviceIdentity : IDisposable
{
    private readonly ECDsa _key;

    private DeviceIdentity(ECDsa key)
    {
        _key = key;
    }

    public string PublicKeyBase64 => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public string Fingerprint
    {
        get
        {
            var hash = SHA256.HashData(_key.ExportSubjectPublicKeyInfo());
            return Convert.ToHexString(hash)[..32];
        }
    }

    public static DeviceIdentity CreateNew()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new DeviceIdentity(key);
    }

    public static DeviceIdentity FromPrivateKey(byte[] pkcs8)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(pkcs8, out _);
        return new DeviceIdentity(key);
    }

    public byte[] ExportPrivateKey() => _key.ExportPkcs8PrivateKey();

    public byte[] Sign(ReadOnlySpan<byte> data)
        => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public static bool Verify(string publicKeyBase64, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose() => _key.Dispose();
}
