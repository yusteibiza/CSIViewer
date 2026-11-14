namespace CSIViewer.Protocol;

/// <summary>Log event types, never injected text (which may be a password).</summary>
public static class InputEventDiagnostics
{
    public static string? SafeAction(InputEventPayload? input) =>
        input?.Kind == "text" ? "[redacted text]" : input?.Action;
}
