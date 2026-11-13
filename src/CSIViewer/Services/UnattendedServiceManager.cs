using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CSIViewer.Services;

public sealed class UnattendedServiceManager
{
    public const string ServiceName = "CSIViewerHost";

    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("sc.exe", $"query {ServiceName}", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return "No instalado";
        }

        // The text of sc.exe is localized; the numeric state code is not.
        var stateMatch = Regex.Match(result.Output, @":\s*([1-7])(?:\s|$)");
        if (stateMatch.Success)
        {
            return int.Parse(stateMatch.Groups[1].Value) switch
            {
                1 => "Detenido",
                2 => "Iniciando",
                3 => "Deteniendo",
                4 => "En ejecución",
                5 => "Continuando",
                6 => "Pausando",
                7 => "Pausado",
                _ => "Instalado"
            };
        }

        var stateLine = result.Output
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains("STATE", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ESTADO", StringComparison.OrdinalIgnoreCase));
        return stateLine is null ? "Instalado" : stateLine[(stateLine.IndexOf(':') + 1)..].Trim();
    }

    public string? FindHostExecutable()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "CSIViewerHost.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++)
        {
            var hostBin = Path.Combine(directory.FullName, "src", "CSIViewer.Host", "bin");
            if (Directory.Exists(hostBin))
            {
                var candidate = Directory.GetFiles(hostBin, "CSIViewerHost.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    public async Task<string> InstallAsync(CancellationToken cancellationToken = default)
    {
        var host = FindHostExecutable();
        if (host is null)
        {
            return "No se encontró CSIViewerHost.exe. Publica o compila el proyecto CSIViewer.Host junto al cliente.";
        }

        var client = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(client) ||
            !string.Equals(Path.GetFileName(client), "CSIViewer.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "CSIViewer.exe");
            if (File.Exists(candidate))
            {
                client = candidate;
            }
        }

        if (string.IsNullOrWhiteSpace(client))
        {
            return "No se pudo determinar la ruta de CSIViewer.exe.";
        }

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $service = '{{ServiceName}}'
            $hostExe = '{{Escape(host)}}'
            $clientExe = '{{Escape(client)}}'
            Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
            sc.exe delete $service 2>$null | Out-Null
            Start-Sleep -Milliseconds 500
            $binPath = '"' + $hostExe + '" --client "' + $clientExe + '" --client-arguments "--host-mode --unattended"'
            New-Service -Name $service -BinaryPathName $binPath -StartupType Automatic -DisplayName "CSIViewer Host" -Description "Modo desatendido de CSIViewer"
            sc.exe failure $service reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
            Start-Service $service
            """;

        var result = await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<string> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $service = '{{ServiceName}}'
            Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
            sc.exe delete $service
            if ($LASTEXITCODE -ne 0) { throw "No se pudo eliminar el servicio (sc.exe $LASTEXITCODE)." }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<string> RunElevatedScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        try
        {
            // The script is passed as an encoded command instead of a file in
            // %TEMP%. A local attacker can no longer replace the script between
            // writing it and the elevated PowerShell reading it.
            var wrapped =
                "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                "try {" + Environment.NewLine +
                script + Environment.NewLine +
                "exit 0" + Environment.NewLine +
                "} catch { exit 1 }" + Environment.NewLine;
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
            return process.ExitCode == 0
                ? "Operación completada."
                : $"La operación falló con el código {process.ExitCode}.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Operación cancelada: no se concedieron permisos de administrador.";
        }
        catch (Exception ex)
        {
            return "No se pudo completar la operación: " + ex.GetBaseException().Message;
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
