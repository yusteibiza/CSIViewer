namespace CSIViewer.Networking;

/// <summary>
/// Prevents accidental plaintext signaling to a remote server. Loopback is
/// always allowed for development; a remote endpoint must use HTTPS/WSS unless
/// the user explicitly accepts the risk.
/// </summary>
public static class ServerEndpointSecurity
{
    public static bool IsSecure(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public static bool IsLocal(Uri uri) =>
        uri.IsLoopback ||
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    public static void EnsureAllowed(string serverUrl, bool allowInsecure)
    {
        var uri = ServerApiClient.NormalizeBaseUri(serverUrl);
        if (IsSecure(uri) || IsLocal(uri) || allowInsecure)
        {
            return;
        }

        throw new InvalidOperationException(
            "El servidor remoto debe usar HTTPS/WSS. " +
            "Activa 'Permitir servidor remoto sin HTTPS' solo en una LAN de confianza.");
    }
}
