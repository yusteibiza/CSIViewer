using CSIViewer.Infrastructure;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class LegacyDataMigrationTests
{
    [TestMethod]
    public void Migrate_CopiesOnlyClientFiles_AndRemovesLegacyDirectory()
    {
        var root = CreateDirectory();
        var legacy = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "data");
        try
        {
            Directory.CreateDirectory(Path.Combine(legacy, "logs"));
            File.WriteAllText(Path.Combine(legacy, "client.db"), "db");
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(legacy, "host.json"), "junk");
            File.WriteAllText(Path.Combine(legacy, "server.json"), "junk");
            File.WriteAllText(Path.Combine(legacy, "logs", "csiviewer.log"), "log");

            LegacyDataMigration.Migrate(
                target,
                new[] { legacy },
                environmentOverride: null,
                LegacyDataMigration.IsClientDataFile);

            Assert.IsTrue(File.Exists(Path.Combine(target, "client.db")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "settings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "logs", "csiviewer.log")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "host.json")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "server.json")));
            Assert.IsFalse(Directory.Exists(legacy));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void Migrate_KeepsServerDatabaseSidecars()
    {
        var root = CreateDirectory();
        var legacy = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "data");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "server.db"), "db");
            File.WriteAllText(Path.Combine(legacy, "server.db-wal"), "wal");
            File.WriteAllText(Path.Combine(legacy, "serversettings.json"), "{}");
            File.WriteAllText(Path.Combine(legacy, "client.db"), "other project");

            LegacyDataMigration.Migrate(
                target,
                new[] { legacy },
                environmentOverride: null,
                LegacyDataMigration.IsServerDataFile);

            Assert.IsTrue(File.Exists(Path.Combine(target, "server.db")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "server.db-wal")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "serversettings.json")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "client.db")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void Migrate_IsSkippedWhenEnvironmentOverrideIsSet()
    {
        var root = CreateDirectory();
        var legacy = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "data");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "client.db"), "db");

            LegacyDataMigration.Migrate(
                target,
                new[] { legacy },
                environmentOverride: target);

            Assert.IsTrue(Directory.Exists(legacy));
            Assert.IsFalse(Directory.Exists(target));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void CleanupObsoleteEntries_RemovesLegacySharedConfiguration()
    {
        var root = CreateDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "host.json"), "junk");
            File.WriteAllText(Path.Combine(root, "window-client.json"), "{}");
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "server-data"));
            File.WriteAllText(Path.Combine(root, "server-data", "hosts.json"), "[]");

            LegacyDataMigration.CleanupObsoleteEntries(root);

            Assert.IsFalse(File.Exists(Path.Combine(root, "host.json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "window-client.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "server-data")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "settings.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CSIViewerMigrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
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
            // Best effort.
        }
    }
}
