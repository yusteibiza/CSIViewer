using CSIViewer.Protocol;

namespace CSIViewer.Application;

/// <summary>Disk-only explorer: no execution, network paths or reparse-point traversal.</summary>
public static class FileBrowser
{
    public const int PageSize = 200;

    public static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(path) || path.StartsWith('\\') ||
            path.IndexOf(':', 2) >= 0 || path.Split('\\', '/').Any(x => x is "." or ".." || x.EndsWith('.') || x.EndsWith(' ')))
            throw new IOException("Ruta local no permitida. No se admiten rutas de red ni enlaces.");
        var full = Path.GetFullPath(path);
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType == DriveType.Network) throw new IOException("Las unidades de red no están disponibles.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se permite seguir enlaces o puntos de reanálisis.");
        }
        return full;
    }

    public static FileBrowseResponse Browse(string path, int offset = 0)
    {
        if (offset is < 0 or > 1000000) throw new IOException("Página no válida.");
        if (string.IsNullOrEmpty(path))
        {
            var roots = DriveInfo.GetDrives().Where(x => x.DriveType != DriveType.Network && x.IsReady)
                .Select(x => new FileBrowserEntry(x.Name, x.Name, true, 0, null)).ToArray();
            return new("", "", null, roots, null);
        }
        var full = ValidatePath(path);
        var entries = new List<FileBrowserEntry>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        // One directory only. Paging bounds the encrypted response and UI allocation.
        var skipped = 0;
        foreach (var item in new DirectoryInfo(full).EnumerateFileSystemInfos("*", options))
        {
            if (string.Equals(item.Name, ".rdpart", StringComparison.OrdinalIgnoreCase)) continue;
            if (skipped++ < offset) continue;
            try
            {
                var folder = (item.Attributes & FileAttributes.Directory) != 0;
                entries.Add(new(item.Name, item.FullName, folder, folder ? 0 : ((FileInfo)item).Length, item.LastWriteTimeUtc));
                if (entries.Count > PageSize) break;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new("", full, Directory.GetParent(full)?.FullName ?? "", entries.Take(PageSize)
            .OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), null,
            entries.Count > PageSize);
    }
}
