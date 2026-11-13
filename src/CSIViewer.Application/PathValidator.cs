namespace CSIViewer.Application;

/// <summary>
/// Validates untrusted relative paths received from a remote peer and keeps
/// every write inside the configured download directory.
/// </summary>
public static class PathValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        global::CSIViewer.Core.Localization.Text("CON"), "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string GetSafeDestinationPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException(global::CSIViewer.Core.Localization.Text("La carpeta de destino no es válida."), nameof(root));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("La ruta del archivo está vacía."));
        }

        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        if (normalized.Length == 0 ||
            normalized.Contains('\0') ||
            Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("Ruta de archivo no permitida."));
        }

        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("Ruta de archivo no permitida."));
        }

        foreach (var part in parts)
        {
            var firstDot = part.IndexOf('.');
            var baseName = firstDot > 0 ? part[..firstDot] : part;
            if (part is "." or ".." ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                part.EndsWith(' ') ||
                part.EndsWith('.') ||
                ReservedNames.Contains(baseName))
            {
                throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("Ruta de archivo no permitida."));
            }
        }

        var relative = string.Join(Path.DirectorySeparatorChar, parts);
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
        EnsureInsideRoot(fullRoot, candidate);
        return candidate;
    }

    public static string EnsureInsideRoot(string root, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("Ruta de archivo no permitida."));
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(fullPath);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("La ruta sale de la carpeta de descargas."));
        }

        return candidate;
    }

    /// <summary>
    /// True when the relative path can be used as a file or directory name
    /// without escaping the configured root.
    /// </summary>
    public static bool IsValidRelativePath(string? relativePath, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return allowEmpty;
        }

        try
        {
            var root = Path.Combine(Path.GetTempPath(), "csiviewer-path-check-" + Guid.NewGuid().ToString("N"));
            GetSafeDestinationPath(root, relativePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
