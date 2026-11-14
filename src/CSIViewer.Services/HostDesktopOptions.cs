namespace CSIViewer.Services;

/// <summary>Explicitly enabled only by the separately provisioned SYSTEM login runtime.</summary>
public sealed class HostDesktopOptions
{
    public bool SystemLoginDesktop { get; init; }
}
