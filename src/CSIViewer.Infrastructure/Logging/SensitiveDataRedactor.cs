using System.Text.RegularExpressions;

namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Defense-in-depth redaction for log messages, exceptions and structured
/// values. Call sites must still avoid logging secrets explicitly.
/// </summary>
public static class SensitiveDataRedactor
{
    private static readonly Regex KeyValueSecret = new(
        @"(?i)\b(password|contrase(?:ñ|n)a|passwd|pwd|token|secret|authorization|bearer|api[_-]?key|enrollment[_-]?code|nonce|session[_-]?key)\b\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerToken = new(
        @"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlCredentials = new(
        @"(?i)(https?://)[^/\s:@]+:[^/\s@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrivateKey = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }

        var result = PrivateKey.Replace(value, "[private key redacted]");
        result = UrlCredentials.Replace(result, "$1***:***@");
        result = BearerToken.Replace(result, "Bearer ***");
        result = KeyValueSecret.Replace(result, "$1=***");
        return result;
    }

    public static bool IsSensitiveKey(string? key) =>
        key is not null &&
        (key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("contraseña", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("enrollment", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("nonce", StringComparison.OrdinalIgnoreCase) ||
         key.Contains("session_key", StringComparison.OrdinalIgnoreCase));
}
