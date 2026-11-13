using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Logging;

namespace CSIViewer.Host.Services;

public static class CrashLog
{
    private const long MaxBytes = 2L * 1024 * 1024;
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(AppPaths.DataDirectory, "ui-crash.log");

    public static void Write(string context, Exception? exception)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (Gate)
            {
                TextLogRotation.RotateIfNeeded(Path, MaxBytes);
                File.AppendAllText(Path,
                    $"[{DateTimeOffset.Now:O}] {SensitiveDataRedactor.Redact(context)}{Environment.NewLine}" +
                    $"{SensitiveDataRedactor.Redact(exception?.ToString())}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Crash logging is best effort.
        }
    }
}
