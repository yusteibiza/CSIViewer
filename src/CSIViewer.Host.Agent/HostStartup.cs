using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace CSIViewer.Host.Agent;

/// <summary>
/// Decides whether CSIViewerHost runs as the unattended Windows service or as
/// an interactive launcher for the host UI. The host UI is part of this
/// installation; the CSIViewer viewer is not required.
/// </summary>
public static class HostStartup
{
    public static bool ShouldRunService(string[] args)
    {
        if (WindowsServiceHelpers.IsWindowsService())
        {
            return true;
        }

        return args.Any(argument =>
            argument.Equals("--service", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--daemon", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--scheduled", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--task", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--once", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--run-once", StringComparison.OrdinalIgnoreCase));
    }

    public static void LaunchHostUi(HostLauncherOptions options)
    {
        try
        {
            var ui = options.UiExecutable;
            if (string.IsNullOrWhiteSpace(ui) || !File.Exists(ui))
            {
                ui = HostUiLocator.Find() ?? "";
            }

            if (string.IsNullOrWhiteSpace(ui) || !File.Exists(ui))
            {
                ShowError(
                    "No se encontró CSIViewer.Host.exe.\n\n" +
                    "Reinstala CSIViewer Host para restaurar la ventana de anfitrión.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = ui,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(ui) ?? AppContext.BaseDirectory
            });
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir CSIViewer Host:\n\n" + ex.Message);
        }
    }

    private static void ShowError(string message) =>
        MessageBoxW(IntPtr.Zero, message, "CSIViewer Host", 0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
