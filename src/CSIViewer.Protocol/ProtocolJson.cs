using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSIViewer.Protocol;

public sealed class Envelope
{
    public int V { get; set; } = 1;
    public string Type { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SessionId { get; set; }
    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;
    public JsonElement Payload { get; set; }
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32
    };

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static Envelope Create(string type, object? payload = null, string? sessionId = null)
    {
        return new Envelope
        {
            Type = type,
            SessionId = sessionId,
            Payload = payload is null
                ? EmptyObject
                : JsonSerializer.SerializeToElement(payload, payload.GetType(), Options)
        };
    }

    public static string Serialize(Envelope envelope) => JsonSerializer.Serialize(envelope, Options);

    public static Envelope? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 64 * 1024)
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
            return envelope is { V: > 0, Type.Length: > 0 } ? envelope : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static T? Payload<T>(Envelope envelope)
    {
        try
        {
            return envelope.Payload.ValueKind == JsonValueKind.Undefined
                ? default
                : envelope.Payload.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
