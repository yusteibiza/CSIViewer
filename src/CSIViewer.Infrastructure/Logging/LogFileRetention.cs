namespace CSIViewer.Infrastructure.Logging;

internal static class LogFileRetention
{
    public static void Cleanup(
        string directory,
        string prefix,
        int retentionDays,
        long maxTotalBytes)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 3650));
            var files = Directory
                .EnumerateFiles(directory, prefix + "-*.log*")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.ToArray())
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    TryDelete(file);
                }
            }

            var remaining = files.Where(file => File.Exists(file.FullName)).ToList();
            var total = remaining.Sum(file => file.Length);
            for (var index = remaining.Count - 1; index > 0 && total > maxTotalBytes; index--)
            {
                var file = remaining[index];
                total -= file.Length;
                TryDelete(file);
            }
        }
        catch
        {
            // Retention is best effort.
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // A log file in use is kept for the next cleanup.
        }
    }
}
