using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CSIViewer.Server;

public sealed record ServerServiceStatus(bool Installed, bool Running, string Description)
{
    public bool IsKnown { get; init; } = true;
}

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
    private readonly Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Output)>> _processRunner;

    public ServerServiceManager() : this(RunProcessAsync) { }

    internal ServerServiceManager(Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Output)>> processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<ServerServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _processRunner(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            Arguments = $"query {ServiceName}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 1060)
        {
            return new ServerServiceStatus(false, false, global::CSIViewer.Core.Localization.Text("No instalado"));
        }
        if (result.ExitCode != 0)
        {
            return new ServerServiceStatus(false, false,
                global::CSIViewer.Core.Localization.Text("No se pudo completar la operación: ") + result.ExitCode)
                { IsKnown = false };
        }

        var stateLine = result.Output
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains("STATE", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ESTADO", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @":\s*[1-7]\s+(STOPPED|START_PENDING|STOP_PENDING|RUNNING|CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b"));
        var match = stateLine is null ? null : Regex.Match(stateLine, @":\s*(\d+)");
        if (match is null || !match.Success || !int.TryParse(match.Groups[1].Value, out var state))
        {
            return new ServerServiceStatus(true, false, global::CSIViewer.Core.Localization.Text("Instalado")) { IsKnown = false };
        }

        return state switch
        {
            4 => new ServerServiceStatus(true, true, global::CSIViewer.Core.Localization.Text("En ejecución")),
            1 => new ServerServiceStatus(true, false, global::CSIViewer.Core.Localization.Text("Detenido")),
            2 => new ServerServiceStatus(true, false, global::CSIViewer.Core.Localization.Text("Iniciando")),
            3 => new ServerServiceStatus(true, false, "Deteniendo"),
            7 => new ServerServiceStatus(true, false, "Pausado"),
            _ => new ServerServiceStatus(true, false, global::CSIViewer.Core.Localization.Text("Instalado"))
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
        if (cancellationToken.IsCancellationRequested)
            return new ServiceOperationResult(false, global::CSIViewer.Core.Localization.Text("Operación cancelada."));
        var exe = FindServerExecutable();
        if (exe is null)
        {
            return new ServiceOperationResult(
                false,
                global::CSIViewer.Core.Localization.Text("No se encontró CSIViewerServidor.exe. Reinstala CSIViewer Servidor."));
        }

        try
        {
            ValidateServiceInstallation(exe);
            return await RunElevatedScriptAsync(BuildInstallScript(exe, settings), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new ServiceOperationResult(false,
                global::CSIViewer.Core.Localization.Text("No se pudo completar la operación: ") + ex.Message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new ServiceOperationResult(false,
                global::CSIViewer.Core.Localization.Text("No se pudo completar la operación: ") + ex.Message);
        }
    }

    internal static void ValidateServiceInstallation(string exe)
    {
        // SYSTEM must never execute a portable/per-user, writable binary tree.
        CSIViewer.Platform.ProtectedInstallation.ValidateProgramFilesParents(exe);
        CSIViewer.Platform.ProtectedInstallation.ValidateTree(Path.GetDirectoryName(Path.GetFullPath(exe))!);
    }

    internal static string BuildInstallScript(string exe, ServerRuntimeSettings settings)
    {
        if (settings.DatabasePath.IndexOfAny(['"', '\0', '\r', '\n']) >= 0 || Path.EndsInDirectorySeparator(settings.DatabasePath))
            throw new ArgumentException(nameof(settings.DatabasePath));
        if (!string.IsNullOrEmpty(settings.CertificateThumbprint) && !settings.CertificateThumbprint.All(Uri.IsHexDigit))
            throw new ArgumentException(nameof(settings.CertificateThumbprint));
        if (string.IsNullOrEmpty(settings.EnrollmentCode) && !string.IsNullOrEmpty(settings.EnrollmentVerifier))
        {
            var parts = settings.EnrollmentVerifier.Split(':');
            Span<byte> saltBytes = stackalloc byte[CSIViewer.Security.PasswordHasher.SaltSize];
            Span<byte> hashBytes = stackalloc byte[CSIViewer.Security.PasswordHasher.HashSize];
            if (parts.Length != 2 || !Convert.TryFromBase64String(parts[0], saltBytes, out var saltLength) || saltLength != saltBytes.Length ||
                !Convert.TryFromBase64String(parts[1], hashBytes, out var hashLength) || hashLength != hashBytes.Length)
                throw new ArgumentException(nameof(settings.EnrollmentVerifier));
        }
        var serviceArguments = new StringBuilder();
        serviceArguments.Append("--modo=servicio");
        serviceArguments.Append(" --port=").Append(settings.Port);
        serviceArguments.Append(" --db=\"").Append(settings.DatabasePath).Append('"');
        if (!string.IsNullOrWhiteSpace(settings.EnrollmentCode))
        {
            var hash = CSIViewer.Security.PasswordHasher.Hash(settings.EnrollmentCode, out var salt);
            serviceArguments.Append(" --enrollment-verifier=\"")
                .Append(salt).Append(':').Append(hash)
                .Append('"');
        }
        else
        {
            // Explicit empty disables enrollment rather than inheriting a
            // different identity's settings/default development code.
            serviceArguments.Append(" --enrollment-verifier=\"").Append(settings.EnrollmentVerifier).Append('"');
        }

        if (settings.ListenOnAllInterfaces)
        {
            serviceArguments.Append(" --all-interfaces");
        }
        else serviceArguments.Append(" --local-only");

        if (settings.EnableUpnp)
        {
            serviceArguments.Append(" --upnp");
        }
        else serviceArguments.Append(" --no-upnp");

        if (settings.UseHttps || !string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            serviceArguments.Append(" --https");
        }
        else serviceArguments.Append(" --no-https");

        if (!string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
        {
            serviceArguments.Append(" --cert-thumbprint=\"")
                .Append(settings.CertificateThumbprint)
                .Append('"');
        }

        if (settings.EnableStunServer)
        {
            serviceArguments.Append(" --stun --stun-port=").Append(settings.StunPort);
        }
        else serviceArguments.Append(" --no-stun");

        serviceArguments.Append(" --max-sessions-per-host=").Append(settings.MaxSessionsPerHost);

        var script = $$"""
            $service = '{{ServiceName}}'
            $exe = '{{Escape(exe)}}'
            $svcArgs = '{{Escape(serviceArguments.ToString())}}'
            $binPath = '"' + $exe + '" ' + $svcArgs
            $existing = Get-Service -Name $service -ErrorAction SilentlyContinue
            if ($existing) {
                if ($existing.Status -ne 'Stopped' -and $existing.Status -ne 'StopPending') { $existing.Stop() }
                $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
                $existing.Dispose()
                & $sc delete $service 2>$null | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed ($LASTEXITCODE)" }
                for ($attempt = 0; $attempt -lt 60; $attempt++) {
                    Start-Sleep -Milliseconds 500
                    if (-not (Get-Service -Name $service -ErrorAction SilentlyContinue)) { break }
                }
                if (Get-Service -Name $service -ErrorAction SilentlyContinue) {
                    throw "El servicio anterior sigue marcado para eliminación. Reinicia Windows o espera unos segundos y vuelve a intentarlo."
                }
            }
            $created = New-Service -Name $service -BinaryPathName $binPath -StartupType Automatic -DisplayName "CSIViewer Servidor" -Description "Servidor de señalización y relay de CSIViewer" -ErrorAction Stop
            & $sc failure $service reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "sc.exe failure failed ($LASTEXITCODE)" }
            $created.Start()
            $created.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
            $status = (Get-Service -Name $service -ErrorAction Stop).Status
            if ($status -ne 'Running') {
                throw "El servicio se creó pero no se pudo iniciar (estado: $status). Revisa el puerto y los permisos de la base de datos."
            }
            """;

        return script;
    }

    public async Task<ServiceOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $service = '{{ServiceName}}'
            $svc = Get-Service -Name $service -ErrorAction SilentlyContinue
            if (-not $svc) {
                throw "El servicio no está creado. Usa «Crear servicio» primero."
            }
            if ($svc.Status -ne 'Running' -and $svc.Status -ne 'StartPending') {
                $svc.Start()
            }
            $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
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
            if ($svc.Status -ne 'Stopped' -and $svc.Status -ne 'StopPending') {
                $svc.Stop()
            }
            $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var script = $$"""
            $service = '{{ServiceName}}'
            $svc = Get-Service -Name $service -ErrorAction Stop
            if ($svc.Status -ne 'Stopped' -and $svc.Status -ne 'StopPending') { $svc.Stop() }
            $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
            $svc.Dispose()
            & $sc delete $service | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "No se pudo eliminar el servicio (sc.exe $LASTEXITCODE)." }
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                Start-Sleep -Milliseconds 300
                if (-not (Get-Service -Name $service -ErrorAction SilentlyContinue)) { break }
            }
            if (Get-Service -Name $service -ErrorAction SilentlyContinue) { throw "Service still marked for deletion" }
            """;

        return await RunElevatedScriptAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private async Task<ServiceOperationResult> RunElevatedScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        var wrapped =
            "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
            "$sc = '" + Escape(Path.Combine(Environment.SystemDirectory, "sc.exe")) + "'" + Environment.NewLine +
            "try {" + Environment.NewLine +
            script + Environment.NewLine +
            "exit 0" + Environment.NewLine +
            "} catch {" + Environment.NewLine +
            "exit 1" + Environment.NewLine +
            "}" + Environment.NewLine;

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            cancellationToken.ThrowIfCancellationRequested();
            // Once launched, SCM mutations cannot be rolled back by canceling
            // a wait. Observe completion instead of reporting false cancellation
            // while an elevated child keeps changing the service in background.
            var result = await _processRunner(startInfo, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return new ServiceOperationResult(true, global::CSIViewer.Core.Localization.Text("Operación completada."));
            }

            return new ServiceOperationResult(
                false,
                global::CSIViewer.Core.Localization.Format("La operación terminó con código {0}. Revisa los permisos de administrador.",result.ExitCode));
        }
        catch (OperationCanceledException)
        {
            return new ServiceOperationResult(false, global::CSIViewer.Core.Localization.Text("Operación cancelada."));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ServiceOperationResult(
                false,
                global::CSIViewer.Core.Localization.Text("Permiso de administrador cancelado; el servicio no se ha modificado."));
        }
        catch (Exception ex)
        {
            return new ServiceOperationResult(
                false,
                global::CSIViewer.Core.Localization.Text("No se pudo completar la operación: ") + ex.GetBaseException().Message);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, "");
            }

            if (startInfo.UseShellExecute)
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                return (process.ExitCode, "");
            }
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return (process.ExitCode, await output.ConfigureAwait(false) + Environment.NewLine + await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { throw; }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
