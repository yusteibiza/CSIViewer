using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Reads the persisted log level before the DI container is built so startup
/// logs already honor the user's selection.
/// </summary>
public static class LoggingBootstrap
{
    public static LogLevel ReadInitialLevel(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return LogLevel.Information;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.TryGetProperty("logLevel", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                Enum.TryParse<CSIViewer.Application.AppLogLevel>(
                    value.GetString(),
                    ignoreCase: true,
                    out var level))
            {
                return level.ToMicrosoft();
            }
        }
        catch
        {
            // Fall back to Information if the settings file is unavailable.
        }

        return LogLevel.Information;
    }
}
