using System.Text;
using CSIViewer.Infrastructure.Logging;

namespace CSIViewer.Server;

public static class ServerServiceLog
{
    private const long MaxBytes = 5L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string BaseDirectory = ServerDataPaths.DataDirectory;
    private static readonly string LogPath = Path.Combine(BaseDirectory, "logs", "server.log");

    public static string LogDirectory => Path.Combine(BaseDirectory, "logs");

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (Gate)
            {
                TextLogRotation.RotateIfNeeded(LogPath, MaxBytes);
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] {SensitiveDataRedactor.Redact(message)}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never stop the service.
        }
    }
}
