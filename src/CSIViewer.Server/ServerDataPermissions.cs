namespace CSIViewer.Server;

/// <summary>
/// Verifies access without widening ACLs on an administrator-selected folder.
/// Provisioning access for the intended service/UI identity is an explicit
/// administrator operation, never a recursive grant to all local users.
/// </summary>
internal static class ServerDataPermissions
{
    public static void EnsureWritable(string databasePath)
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

        throw new UnauthorizedAccessException(global::CSIViewer.Core.Localization.Text("No se pudo completar la operación: ") + databasePath);
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
