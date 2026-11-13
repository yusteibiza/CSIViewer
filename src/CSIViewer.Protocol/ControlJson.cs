using System.Text.Json;

namespace CSIViewer.Protocol;

public sealed record ControlEnvelope(string Type, JsonElement Payload);

public static class ControlJson
{
    public static byte[] Create(string type, object? payload = null)
    {
        var envelope = new ControlEnvelope(
            type,
            payload is null
                ? JsonDocument.Parse("{}").RootElement.Clone()
                : JsonSerializer.SerializeToElement(payload, payload.GetType(), ProtocolJson.Options));
        return JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Options);
    }

    public static bool TryRead(ReadOnlySpan<byte> data, out ControlEnvelope? envelope)
    {
        envelope = null;
        try
        {
            envelope = JsonSerializer.Deserialize<ControlEnvelope>(data, ProtocolJson.Options);
            return envelope is { Type.Length: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static T? Payload<T>(ControlEnvelope envelope) where T : class
    {
        try
        {
            return envelope.Payload.Deserialize<T>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
