using CSIViewer.Infrastructure;

namespace CSIViewer.Server;

/// <summary>
/// Portable server data root. It lives next to the executable and falls back
/// to ProgramData only when that folder is not writable.
/// </summary>
public static class ServerDataPaths
{
    private static readonly Lazy<string> Directory = new(ResolveDataDirectory);

    public static string DataDirectory => Directory.Value;

    public static string MigrateDatabasePath(string databasePath)
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var full = Path.GetFullPath(databasePath);
            var legacyRoots = new[]
            {
                Path.Combine(local, "CSIViewerServidor"),
                Path.Combine(local, "CSIViewerServer"),
                Path.Combine(local, "RemoteDesktopServer"),
                Path.Combine(local, "CSIViewer")
            };

            foreach (var root in legacyRoots)
            {
                if (full.StartsWith(
                        Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(DataDirectory, "server.db");
                }
            }
        }
        catch
        {
            // Keep the configured path if it cannot be normalized.
        }

        return databasePath;
    }

    private static string ResolveDataDirectory()
    {
        var environment =
            Environment.GetEnvironmentVariable("CSIVIEWER_SERVER_DATA_DIR") ??
            Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return Path.GetFullPath(environment);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var target = LegacyDataMigration.ResolveWritableDirectory(
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(common, "CSIViewerServidor"));

        LegacyDataMigration.Migrate(
            target,
            new[]
            {
                Path.Combine(local, "CSIViewerServidor"),
                Path.Combine(local, "CSIViewerServer"),
                Path.Combine(local, "RemoteDesktopServer"),
                Path.Combine(local, "CSIViewer")
            },
            environment,
            LegacyDataMigration.IsServerDataFile);

        if (string.IsNullOrWhiteSpace(environment))
        {
            LegacyDataMigration.CleanupObsoleteEntries(target);
        }

        return target;
    }
}
