namespace CSIViewer.Security;

public static class CredentialExpiry
{
    public static DateTimeOffset? Calculate(DateTimeOffset now, int lifetimeMinutes) =>
        lifetimeMinutes > 0 ? now.AddMinutes(lifetimeMinutes) : null;

    public static bool IsExpired(DateTimeOffset? expiresUtc, DateTimeOffset now) =>
        expiresUtc is { } expiry && now >= expiry;
}
