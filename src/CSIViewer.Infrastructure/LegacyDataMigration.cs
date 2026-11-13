namespace CSIViewer.Infrastructure;

/// <summary>
/// One-time migration from the old LocalAppData locations to the portable
/// data directory next to the executable. Legacy directories are removed only
/// after their contents have been copied or when the target already exists.
/// </summary>
public static class LegacyDataMigration
{
    public static void Migrate(
        string targetDirectory,
        IEnumerable<string> legacyDirectories,
        string? environmentOverride,
        Func<string, bool>? include = null)
    {
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            // Tests and portable deployments explicitly choose their data root.
            return;
        }

        try
        {
            var target = Path.GetFullPath(targetDirectory);
            var targetStartedEmpty = !Directory.Exists(target) ||
                                     !Directory.EnumerateFileSystemEntries(target).Any();

            foreach (var legacy in legacyDirectories)
            {
                if (string.IsNullOrWhiteSpace(legacy) || !Directory.Exists(legacy))
                {
                    continue;
                }

                var source = Path.GetFullPath(legacy);
                if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (targetStartedEmpty)
                    {
                        CopyDirectory(source, target, include);
                    }

                    Directory.Delete(source, recursive: true);
                }
                catch
                {
                    // If copying or deleting fails, keep the legacy data.
                }
            }
        }
        catch
        {
            // Migration is best effort.
        }
    }

    /// <summary>
    /// Returns true for the files that belong to the client: database,
    /// identity, settings, crash log and structured logs.
    /// </summary>
    public static bool IsClientDataFile(string relativePath)
    {
        if (IsStructuredLog(relativePath))
        {
            return true;
        }

        var name = Path.GetFileName(relativePath);
        return name.Equals("client.db", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("client.db-", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("identity.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ui-crash.log", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true for the files that belong to the server: database, server
    /// settings, crash log and structured logs.
    /// </summary>
    public static bool IsServerDataFile(string relativePath)
    {
        if (IsStructuredLog(relativePath))
        {
            return true;
        }

        var name = Path.GetFileName(relativePath);
        return name.Equals("server.db", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("server.db-", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("serversettings.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ui-crash.log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStructuredLog(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.StartsWith("logs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] ObsoleteFileNames =
    {
        "host.json",
        "server.json",
        "theme-host.json",
        "theme-server.json",
        "window-client.json",
        "window-host.json",
        "window-server.json"
    };

    private static readonly string[] ObsoleteDirectoryNames =
    {
        "server-data"
    };

    /// <summary>
    /// Removes configuration files left by older shared builds. They are no
    /// longer read: window placement lives in settings and server state uses
    /// SQLite. Only the portable data root is cleaned.
    /// </summary>
    public static void CleanupObsoleteEntries(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var name in ObsoleteFileNames)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            foreach (var name in ObsoleteDirectoryNames)
            {
                var path = Path.Combine(directory, name);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
        catch
        {
            // Cleanup is best effort.
        }
    }

    public static string ResolveWritableDirectory(string preferred, string fallback)
    {
        if (TryPrepare(preferred))
        {
            return preferred;
        }

        return TryPrepare(fallback) ? fallback : preferred;
    }

    public static bool IsWritableDirectory(string directory) => TryPrepare(directory);

    private static bool TryPrepare(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
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

    private static void CopyDirectory(string source, string target, Func<string, bool>? include)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                directory.Replace(source, target, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (include is not null && !include(relative))
            {
                continue;
            }

            var destination = file.Replace(source, target, StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }
    }
}
