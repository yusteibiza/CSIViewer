using System.Diagnostics;
using System.Text;

namespace CSIViewer.Host.Services;

/// <summary>
/// Manages the scheduled task that starts the host at Windows boot. A task
/// running as SYSTEM replaces the previous Windows service, so UAC is needed
/// only when the task is created or removed.
/// </summary>
public sealed class UnattendedServiceManager
{
    public const string TaskName = "CSIViewerHost";
    private const string LegacyServiceName = "CSIViewerHost";

    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            "schtasks.exe",
            $"/Query /TN {TaskName} /FO LIST",
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return "No instalado";
        }

        var output = result.Output;
        if (output.Contains("Running", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("En ejecución", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("En ejecucion", StringComparison.OrdinalIgnoreCase))
        {
            return "En ejecución";
        }

        return "Listo";
    }

    public string? FindHostExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "agent", "CSIViewer.Host.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "CSIViewer.Host.Agent.exe")
        };

        var parent = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            candidates.Add(Path.Combine(parent, "agent", "CSIViewer.Host.Agent.exe"));
            candidates.Add(Path.Combine(parent, "CSIViewer.Host.Agent.exe"));
        }

        // Development layout: the UI and the startup agent are separate projects.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++)
        {
            var hostBin = Path.Combine(directory.FullName, "src", "CSIViewer.Host.Agent", "bin");
            if (Directory.Exists(hostBin))
            {
                var candidate = Directory
                    .GetFiles(hostBin, "CSIViewer.Host.Agent.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            directory = directory.Parent;
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<string> InstallAsync(CancellationToken cancellationToken = default)
    {
        var host = FindHostExecutable();
        if (host is null)
        {
            return "No se encontró el agente de inicio CSIViewer.Host.Agent.exe. Reinstala CSIViewer Host.";
        }

        var script = $$"""
            $task = '{{TaskName}}'
            $legacy = '{{LegacyServiceName}}'
            $hostExe = '{{Escape(host)}}'
            Stop-Service -Name $legacy -Force -ErrorAction SilentlyContinue
            sc.exe delete $legacy 2>$null | Out-Null
            $action = New-ScheduledTaskAction -Execute $hostExe -Argument '--scheduled'
            $trigger = New-ScheduledTaskTrigger -AtStartup
            $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Inicia CSIViewer Host al arrancar Windows' -Force | Out-Null
            Start-ScheduledTask -TaskName $task
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $task = '{{TaskName}}'
            $legacy = '{{LegacyServiceName}}'
            schtasks.exe /Delete /TN $task /F 2>$null | Out-Null
            Stop-Service -Name $legacy -Force -ErrorAction SilentlyContinue
            sc.exe delete $legacy 2>$null | Out-Null
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<string> RunElevatedScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        var resultFile = Path.Combine(
            Path.GetTempPath(),
            "csiviewer-host-task-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            // The script is passed as an encoded command instead of a file in
            // %TEMP%. A local attacker can no longer replace the script between
            // writing it and the elevated PowerShell reading it.
            var wrapped =
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                "try {" + Environment.NewLine +
                script + Environment.NewLine +
                "Set-Content -LiteralPath '" + Escape(resultFile) + "' -Value 'OK' -Encoding UTF8" + Environment.NewLine +
                "exit 0" + Environment.NewLine +
                "} catch {" + Environment.NewLine +
                "Set-Content -LiteralPath '" + Escape(resultFile) + "' -Value ($_.Exception.Message) -Encoding UTF8" + Environment.NewLine +
                "exit 1" + Environment.NewLine +
                "}" + Environment.NewLine;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "No se pudo iniciar PowerShell con permisos de administrador.";
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return "Operación completada.";
            }

            var detail = ReadResultFile(resultFile);
            return string.IsNullOrWhiteSpace(detail)
                ? $"La operación terminó con código {process.ExitCode}. Revisa los permisos de administrador."
                : detail;
        }
        catch (OperationCanceledException)
        {
            return "Operación cancelada.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Permiso de administrador cancelado; no se ha cambiado nada.";
        }
        catch (Exception ex)
        {
            return "No se pudo completar la operación: " + ex.GetBaseException().Message;
        }
        finally
        {
            try
            {
                File.Delete(resultFile);
            }
            catch
            {
                // The temporary result file is best effort.
            }
        }
    }

    private static string ReadResultFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, "");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, output + Environment.NewLine + error);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
