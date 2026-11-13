namespace CSIViewer.Infrastructure;

public static class AppPaths
{
    private static readonly Lazy<string> DefaultDataDirectory = new(ResolveDefaultDataDirectory);
    private static string? _override;

    /// <summary>
    /// Allows the host launcher to point the client at the host's portable
    /// data directory before the service container is built.
    /// </summary>
    public static void SetDataDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var candidate = Path.GetFullPath(path.Trim().Trim('"'));
        // Ignore the host-provided path if it is not writable (for example a
        // read-only installation folder). The normal resolution then applies.
        _override = LegacyDataMigration.IsWritableDirectory(candidate) ? candidate : null;
    }

    public static string DataDirectory
    {
        get
        {
            var path = _override ?? DefaultDataDirectory.Value;
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "client.db");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string IdentityPath => Path.Combine(DataDirectory, "identity.json");
    public static string LogsDirectory
    {
        get
        {
            var path = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private static string ResolveDefaultDataDirectory()
    {
        var environment = Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return Path.GetFullPath(environment);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var target = LegacyDataMigration.ResolveWritableDirectory(
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(local, global::CSIViewer.Core.Localization.Text("CSIViewer")));

        LegacyDataMigration.Migrate(
            target,
            new[]
            {
                Path.Combine(local, global::CSIViewer.Core.Localization.Text("CSIViewer")),
                Path.Combine(local, "RemoteDesktopClient"),
                Path.Combine(local, "RemoteDesktop"),
                Path.Combine(local, global::CSIViewer.Core.Localization.Text("CSIViewer"))
            },
            environment,
            LegacyDataMigration.IsClientDataFile);

        if (string.IsNullOrWhiteSpace(environment))
        {
            LegacyDataMigration.CleanupObsoleteEntries(target);
        }

        return target;
    }
}
