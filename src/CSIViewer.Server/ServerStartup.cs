using CSIViewer.Infrastructure;

namespace CSIViewer.Server;

/// <summary>
/// Windows auto-start for the server UI. The registered command explicitly
/// uses UI mode so no console window is shown at sign-in.
/// </summary>
public static class ServerStartup
{
    private static readonly StartupService Service =
        new("CSIViewerServidor", "--modo=ui", ResolveServerExecutable());

    public static bool IsEnabled => Service.IsEnabled;

    public static bool SetEnabled(bool enabled) => Service.SetEnabled(enabled);

    private static string? ResolveServerExecutable()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(process) &&
            string.Equals(
                Path.GetFileName(process),
                "CSIViewerServidor.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return process;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "CSIViewerServidor.exe");
        return File.Exists(candidate) ? candidate : process;
    }
}
