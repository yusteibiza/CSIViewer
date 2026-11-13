using CSIViewer.Infrastructure.Logging;

namespace CSIViewer.Server;

public static class CrashLog
{
    private const long MaxBytes = 2L * 1024 * 1024;
    private static readonly object Gate = new();

    public static void Write(string context, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(ServerSettingsStore.DataDirectory);
            var path = Path.Combine(ServerSettingsStore.DataDirectory, "ui-crash.log");
            lock (Gate)
            {
                TextLogRotation.RotateIfNeeded(path, MaxBytes);
                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:O}] {SensitiveDataRedactor.Redact(context)}{Environment.NewLine}" +
                    $"{SensitiveDataRedactor.Redact(exception?.ToString())}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // A crash log must never crash the process.
        }
    }
}
