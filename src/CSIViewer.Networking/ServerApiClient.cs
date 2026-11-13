using System.Net.Http.Json;
using System.Text.Json;
using CSIViewer.Protocol;

namespace CSIViewer.Networking;

public sealed class ServerApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ServerApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<RegisterDeviceResponse> RegisterAsync(
        string serverUrl,
        string name,
        string publicKeyBase64,
        string enrollmentCode,
        CancellationToken cancellationToken = default)
    {
        var baseUri = NormalizeBaseUri(serverUrl);
        var request = new RegisterDeviceRequest(name, publicKeyBase64, enrollmentCode);
        using var response = await _http.PostAsJsonAsync(new Uri(baseUri, "api/devices/register"), request, _json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"El servidor rechazó el registro ({(int)response.StatusCode}): {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>(_json, cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("El servidor devolvió una respuesta de registro vacía.");
    }

    public async Task<bool> CheckHealthAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUri = NormalizeBaseUri(serverUrl);
            using var response = await _http.GetAsync(new Uri(baseUri, "api/health"), cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static Uri BuildWebSocketUri(string serverUrl)
    {
        var baseUri = NormalizeBaseUri(serverUrl);
        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws"
        };
        return builder.Uri;
    }

    public static Uri NormalizeBaseUri(string serverUrl)
    {
        var value = (serverUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "http://localhost:5080";
        }

        // Accept WebSocket-style URLs as input, but normalize them to the
        // equivalent HTTP scheme so API and WS endpoints cannot downgrade.
        if (value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value[6..];
        }
        else if (value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            value = "http://" + value[5..];
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return new Uri(value);
    }
}
