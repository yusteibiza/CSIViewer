namespace CSIViewer.Infrastructure.Logging;

/// <summary>
/// Keeps the small plain-text logs used during service startup and crash
/// handling bounded. Structured logs use JsonLinesFileLoggerProvider instead.
/// </summary>
public static class TextLogRotation
{
    public static void RotateIfNeeded(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < maxBytes)
            {
                return;
            }

            File.Move(path, path + ".1", overwrite: true);
        }
        catch
        {
            // Rotation is best effort; logging must never fail because of it.
        }
    }
}
