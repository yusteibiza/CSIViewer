using System.Diagnostics;

namespace CSIViewer.Server;

/// <summary>
/// Makes sure the server database can be written by the interactive user.
/// The service runs as SYSTEM and may create the database and its WAL files
/// with an ACL that only allows SYSTEM/Administrators; the UI process then
/// fails with "attempt to write a readonly database". When running elevated
/// (the service), this best-effort step grants local Users modify rights on
/// the dedicated data directory so both modes can share the database.
/// </summary>
internal static class ServerDataPermissions
{
    private const string UsersSid = "*S-1-5-32-545";

    public static void EnsureWritable(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            if (CanWrite(databasePath))
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = $"\"{directory}\" /grant {UsersSid}:(OI)(CI)M /T /C",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
        catch
        {
            // Permission repair is best effort; the server can still relay
            // sessions even when history cannot be persisted.
        }
    }

    private static bool CanWrite(string databasePath)
    {
        if (!CanWriteFile(databasePath, create: true))
        {
            return false;
        }

        // SQLite needs to write the WAL and shared-memory files too. They are
        // created by whichever process opens the database first (often the
        // SYSTEM service), so their ACL can differ from the main database.
        return CanWriteFile(databasePath + "-wal", create: false) &&
               CanWriteFile(databasePath + "-shm", create: false);
    }

    private static bool CanWriteFile(string path, bool create)
    {
        try
        {
            if (!create && !File.Exists(path))
            {
                return true;
            }

            using var stream = new FileStream(
                path,
                create ? FileMode.OpenOrCreate : FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
