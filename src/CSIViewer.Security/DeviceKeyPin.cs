namespace CSIViewer.Security;

/// <summary>
/// Trust-on-first-use pinning for peer device keys. The first successful
/// connection stores the peer public key; later connections must present the
/// same key or the user must explicitly forget the known device.
/// </summary>
public static class DeviceKeyPin
{
    public static bool IsConsistent(string? knownPublicKey, string presentedPublicKey)
    {
        if (string.IsNullOrWhiteSpace(knownPublicKey))
        {
            return true;
        }

        return string.Equals(knownPublicKey, presentedPublicKey, StringComparison.Ordinal);
    }
}
