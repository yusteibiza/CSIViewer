namespace CSIViewer.Host.Agent;

/// <summary>
/// Locates the CSIViewer host UI executable that belongs to this installation.
/// The host is self-contained: it does not look for, nor require, the CSIViewer
/// viewer to be installed on the machine.
/// </summary>
public static class HostUiLocator
{
    public const string UiExecutableName = "CSIViewer.Host.exe";

    public static string? Find()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var candidates = new List<string>();
        AddDirectory(candidates, baseDirectory);
        AddDirectory(candidates, Path.Combine(baseDirectory, "ui"));

        var parent = Directory.GetParent(baseDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            AddDirectory(candidates, parent);
            AddDirectory(candidates, Path.Combine(parent, "ui"));

            var grandParent = Directory.GetParent(parent)?.FullName;
            if (!string.IsNullOrWhiteSpace(grandParent))
            {
                AddDirectory(candidates, grandParent);
                AddDirectory(candidates, Path.Combine(grandParent, "ui"));
            }
        }

        AddDevelopmentLayout(candidates);

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AddDirectory(List<string> candidates, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        candidates.Add(Path.Combine(directory, UiExecutableName));
    }

    private static void AddDevelopmentLayout(List<string> candidates)
    {
        try
        {
            var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var directory = new DirectoryInfo(baseDirectory);
            for (var depth = 0; directory is not null && depth < 8; depth++)
            {
                var uiBin = Path.Combine(
                    directory.FullName,
                    "src",
                    "CSIViewer.Host",
                    "bin");
                if (Directory.Exists(uiBin))
                {
                    var files = Directory.GetFiles(
                        uiBin,
                        UiExecutableName,
                        SearchOption.AllDirectories);
                    var preferDebug = AppContext.BaseDirectory.Contains(
                        "\\Debug\\",
                        StringComparison.OrdinalIgnoreCase);
                    var candidate = files
                        .OrderByDescending(path => preferDebug == path.Contains(
                            "\\Debug\\",
                            StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }

                directory = directory.Parent;
            }
        }
        catch
        {
            // Development lookup is best effort.
        }
    }
}
