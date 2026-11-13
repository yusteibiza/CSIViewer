using Microsoft.Extensions.Logging;

namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Mutable logging configuration shared by the file provider and the UI.
/// Changes take effect immediately without rebuilding the logger factory.
/// </summary>
public sealed class LoggingOptions
{
    private int _minimumLevel = (int)LogLevel.Information;

    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, (int)value);
    }

    public long MaxFileBytes { get; set; } = 10L * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 200L * 1024 * 1024;
    public int RetentionDays { get; set; } = 30;
}

public static class LogLevelMapping
{
    public static LogLevel ToMicrosoft(this CSIViewer.Application.AppLogLevel level) => level switch
    {
        CSIViewer.Application.AppLogLevel.Debug => LogLevel.Debug,
        CSIViewer.Application.AppLogLevel.Warning => LogLevel.Warning,
        CSIViewer.Application.AppLogLevel.Error => LogLevel.Error,
        CSIViewer.Application.AppLogLevel.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };
}
