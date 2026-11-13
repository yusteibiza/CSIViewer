using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CSIViewer.Server;

public sealed record ServerServiceStatus(bool Installed, bool Running, string Description);

public sealed record ServiceOperationResult(bool Success, string Message);

/// <summary>
/// Installs or removes the Windows service for the server. The operation is
/// elevated through UAC; once installed, the service starts with Windows and
/// does not require the user to open the UI.
/// </summary>
public sealed class ServerServiceManager
{
    public const string ServiceName = "CSIViewerServidor";
    private const int ErrorCancelled = 1223;

    public async Task<ServerServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("sc.exe", $"query {ServiceName}", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.Output.Contains("1060", StringComparison.Ordinal))
        {
            return new ServerServiceStatus(false, false, "No instalado");
        }

        var stateLine = result.Output
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains("STATE", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ESTADO", StringComparison.OrdinalIgnoreCase));
        var match = stateLine is null ? null : Regex.Match(stateLine, @":\s*(\d+)");
        if (match is null || !match.Success || !int.TryParse(match.Groups[1].Value, out var state))
        {
            return new ServerServiceStatus(true, false, "Instalado");
        }

        return state switch
        {
            4 => new ServerServiceStatus(true, true, "En ejecución"),
            1 => new ServerServiceStatus(true, false, "Detenido"),
            2 => new ServerServiceStatus(true, false, "Iniciando"),
            3 => new ServerServiceStatus(true, false, "Deteniendo"),
            7 => new ServerServiceStatus(true, false, "Pausado"),
            _ => new ServerServiceStatus(true, false, "Instalado")
        };
    }

    public string? FindServerExecutable()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(process) &&
            string.Equals(Path.GetFileName(process), "CSIViewerServidor.exe", StringComparison.OrdinalIgnoreCase))
        {
            return process;
        }

        var direct = Path.Combine(AppContext.BaseDirectory, "CSIViewerServidor.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++)
        {
            var bin = Path.Combine(directory.FullName, "src", "CSIViewer.Server", "bin");
            if (Directory.Exists(bin))
            {
                var candidate = Directory
                    .GetFiles(bin, "CSIViewerServidor.exe", SearchOption.AllDirectories)
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

    public async Task<ServiceOperationResult> InstallAsync(
        ServerRuntimeSettings settings,
        CancellationToken cancellationToken = default)
    {
        var exe = FindServerExecutable();
        if (exe is null)
        {
            return new ServiceOperationResult(
                false,
                "No se encontró CSIViewerServidor.exe. Reinstala CSIViewer Servidor.");
        }

        var serviceArguments = new StringBuilder();
        serviceArguments.Append("--modo=servicio");
        serviceArguments.Append(" --port=").Append(settings.Port);
        serviceArguments.Append(" --db=\"").Append(Escape(settings.DatabasePath)).Append('"');
        if (!string.IsNullOrWhiteSpace(settings.EnrollmentCode))
        {
            serviceArguments.Append(" --enrollment-code=\"")
                .Append(Escape(settings.EnrollmentCode))
                .Append('"');
        }

        if (settings.ListenOnAllInterfaces)
        {
            serviceArguments.Append(" --all-interfaces");
        }

        if (settings.EnableUpnp)
        {
            serviceArguments.Append(" --upnp");
        }

        if (settings.UseHttps || !string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            serviceArguments.Append(" --https");
        }

        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            serviceArguments.Append(" --cert-thumbprint=\"")
                .Append(Escape(settings.CertificateThumbprint))
                .Append('"');
        }

        if (settings.EnableStunServer)
        {
            serviceArguments.Append(" --stun --stun-port=").Append(settings.StunPort);
        }

        serviceArguments.Append(" --max-sessions-per-host=").Append(settings.MaxSessionsPerHost);

        var script = $$"""
            $service = '{{ServiceName}}'
            $exe = '{{Escape(exe)}}'
            $svcArgs = '{{Escape(serviceArguments.ToString())}}'
            $binPath = '"' + $exe + '" ' + $svcArgs
            $existing = Get-Service -Name $service -ErrorAction SilentlyContinue
            if ($existing) {
                Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
                sc.exe delete $service 2>$null | Out-Null
                for ($attempt = 0; $attempt -lt 60; $attempt++) {
                    Start-Sleep -Milliseconds 500
                    if (-not (Get-Service -Name $service -ErrorAction SilentlyContinue)) { break }
                }
                if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
                    throw "El servicio anterior sigue marcado para eliminación. Reinicia Windows o espera unos segundos y vuelve a intentarlo."
                }
            }
            New-Service -Name $service -BinaryPathName $binPath -StartupType Automatic -DisplayName "CSIViewer Servidor" -Description "Servidor de señalización y relay de CSIViewer" -ErrorAction Stop
            sc.exe failure $service reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
            Start-Service -Name $service -ErrorAction Stop
            $status = (Get-Service -Name $service -ErrorAction Stop).Status
            if ($status -ne 'Running') {
                throw "El servicio se creó pero no se pudo iniciar (estado: $status). Revisa el puerto y los permisos de la base de datos."
            }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $service = '{{ServiceName}}'
            $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
            if (-not $svc) {
                throw "El servicio no está creado. Usa «Crear servicio» primero."
            }
            if ($svc.Status -ne 'Running') {
                Start-Service -Name $service -ErrorAction Stop
            }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $service = '{{ServiceName}}'
            $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
            if (-not $svc) {
                throw "El servicio no está creado."
            }
            if ($svc.Status -ne 'Stopped') {
                Stop-Service -Name $service -Force -ErrorAction Stop
            }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $service = '{{ServiceName}}'
            Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
            sc.exe delete $service | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "No se pudo eliminar el servicio (sc.exe $LASTEXITCODE)." }
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                Start-Sleep -Milliseconds 300
                if (-not (Get-Service -Name $service -ErrorAction SilentlyContinue)) { break }
            }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<ServiceOperationResult> RunElevatedScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        var resultFile = Path.Combine(
            Path.GetTempPath(),
            "csiviewer-service-" + Guid.NewGuid().ToString("N") + ".txt");

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

        try
        {
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
                return new ServiceOperationResult(
                    false,
                    "No se pudo iniciar PowerShell con permisos de administrador.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var detail = ReadResultFile(resultFile);
            if (process.ExitCode == 0)
            {
                return new ServiceOperationResult(true, "Operación completada.");
            }

            return new ServiceOperationResult(
                false,
                string.IsNullOrWhiteSpace(detail)
                    ? $"La operación terminó con código {process.ExitCode}. Revisa los permisos de administrador."
                    : detail);
        }
        catch (OperationCanceledException)
        {
            return new ServiceOperationResult(false, "Operación cancelada.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ServiceOperationResult(
                false,
                "Permiso de administrador cancelado; el servicio no se ha modificado.");
        }
        catch (Exception ex)
        {
            return new ServiceOperationResult(
                false,
                "No se pudo completar la operación: " + ex.GetBaseException().Message);
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
