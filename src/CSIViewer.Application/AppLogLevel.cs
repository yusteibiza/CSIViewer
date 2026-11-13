namespace CSIViewer.Application;

/// <summary>
/// Verbosity selected by the user. It is stored in settings and mapped to the
/// Microsoft.Extensions.Logging level used by the file logger.
/// </summary>
public enum AppLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
