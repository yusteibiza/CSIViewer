namespace CSIViewer.Services;

/// <summary>Application-specific chat lifetime; viewers retain history by default.</summary>
public sealed class ChatHistoryOptions
{
    public bool RetainHistory { get; init; } = true;
}
