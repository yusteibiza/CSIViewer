using System.Security.Cryptography;
using System.Text;

namespace CSIViewer.Security;

/// <summary>
/// Intercambio ECDH P-256 con transcripcion firmada por las identidades de los dispositivos.
/// No sustituye una revision criptografica formal, pero usa exclusivamente primitivas de la BCL.
/// </summary>
public sealed class SessionHandshake : IDisposable
{
    private readonly ECDiffieHellman _ecdh;
    private readonly string _nonce;

    public SessionHandshake()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public string PublicKeyBase64 => Convert.ToBase64String(_ecdh.ExportSubjectPublicKeyInfo());
    public string Nonce => _nonce;

    public static byte[] BuildClientTranscript(
        string sessionId,
        string controllerDeviceId,
        string hostDeviceId,
        string controllerEphemeralPublicKey,
        string controllerNonce,
        int permissions)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"RD-SESSION-CLIENT-v1|{sessionId}|{controllerDeviceId}|{hostDeviceId}|{controllerEphemeralPublicKey}|{controllerNonce}|{permissions}"));
    }

    public static byte[] BuildFinalTranscript(
        string sessionId,
        string controllerDeviceId,
        string hostDeviceId,
        string controllerEphemeralPublicKey,
        string hostEphemeralPublicKey,
        string controllerNonce,
        string hostNonce,
        int permissions)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"RD-SESSION-FINAL-v1|{sessionId}|{controllerDeviceId}|{hostDeviceId}|{controllerEphemeralPublicKey}|{hostEphemeralPublicKey}|{controllerNonce}|{hostNonce}|{permissions}"));
    }

    public SessionKeys DeriveKeys(string remotePublicKeyBase64, byte[] transcript)
    {
        using var remote = ECDiffieHellman.Create();
        remote.ImportSubjectPublicKeyInfo(Convert.FromBase64String(remotePublicKeyBase64), out _);

        var shared = _ecdh.DeriveRawSecretAgreement(remote.PublicKey);
        var salt = SHA256.HashData(transcript);

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, shared, salt);
        var c2h = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, Encoding.UTF8.GetBytes("rd/v1/control/c2h"));
        var h2c = HKDF.Expand(HashAlgorithmName.SHA256, prk, 32, Encoding.UTF8.GetBytes("rd/v1/control/h2c"));
        CryptographicOperations.ZeroMemory(shared);
        CryptographicOperations.ZeroMemory(prk);
        return new SessionKeys(c2h, h2c, salt);
    }

    public void Dispose() => _ecdh.Dispose();
}
