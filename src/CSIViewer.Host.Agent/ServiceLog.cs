using System.Text;

namespace CSIViewer.Host.Agent;

public static class ServiceLog
{
    private const long MaxBytes = 5L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogPath = ResolveLogPath();

    public static void Write(string message)
    {
        try
        {
            EnsureDirectory();
            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never stop the service.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length >= MaxBytes)
            {
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            }
        }
        catch
        {
            // Rotation is best effort.
        }
    }

    public static string Path => LogPath;

    private static string ResolveLogPath()
    {
        var localHost = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CSIViewerHost");
        var commonHost = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CSIViewerHost");
        try
        {
            var preferred = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "logs");
            if (CanWrite(preferred))
            {
                CleanupLegacyDirectories(localHost, commonHost);
                return System.IO.Path.Combine(preferred, "host.log");
            }
        }
        catch
        {
            // Fall back to ProgramData.
        }

        CleanupLegacyDirectories(localHost);
        return System.IO.Path.Combine(commonHost, "logs", "host.log");
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = System.IO.Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupLegacyDirectories(params string[] directories)
    {
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Old service logs are best effort cleanup.
            }
        }
    }

    private static void EnsureDirectory()
    {
        var directory = System.IO.Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
