using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Platform;

#if CSIVIEWER_HOST
namespace CSIViewer.Host.Services;
#else
namespace CSIViewer.Services;
#endif

/// <summary>One scheduled-task implementation shared by the independent host and viewer.</summary>
public sealed class UnattendedServiceManager
{
    public const string TaskName = "CSIViewerHost";
    public const string ServiceName = TaskName; // Compatibility; no service is installed.
    private HostStartupStatus? _confirmed;
    public bool LastOperationSucceeded { get; private set; }
    public HostStartupStatus? LastConfirmedStatus => _confirmed;

    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default) =>
        (await GetStatusDetailsAsync(false, cancellationToken).ConfigureAwait(false)).Text;

    public async Task<HostStartupStatus> GetStatusDetailsAsync(bool allowElevation = false, CancellationToken cancellationToken = default)
    {
        HostStartupStatus status;
        try { status = await Task.Run(ReadStatus, cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
        catch { status = new(HostStartupState.Unknown); }
        if (status.State == HostStartupState.Unknown && allowElevation)
        {
            var code = await RunElevatedAsync("", cancellationToken).ConfigureAwait(false);
            status = Decode(code);
        }
        if (status.State != HostStartupState.Unknown) { _confirmed = status; return status; }
        return _confirmed is not null ? _confirmed with { LastConfirmed = true } : status;
    }

    private static HostStartupStatus ReadStatus()
    {
        object? service = null, folder = null, task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException();
            service = Activator.CreateInstance(type) ?? throw new InvalidOperationException();
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            try { task = ((dynamic)folder).GetTask(TaskName); }
            // COM interop can map ERROR_FILE_NOT_FOUND to FileNotFoundException,
            // not only COMException. Only task lookup's missing-path HRESULTs count.
            catch (Exception ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003))
            { return new(HostStartupState.NotInstalled); }
            if (!(bool)((dynamic)task).Enabled) return new(HostStartupState.Disabled);
            return new((int)((dynamic)task).State switch
            {
                2 or 3 => HostStartupState.Ready, 4 => HostStartupState.Running, _ => HostStartupState.Unknown
            });
        }
        catch { return new(HostStartupState.Unknown); } // Access denied is NOT task absence.
        finally
        {
            foreach (var value in new[] { task, folder, service })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }

    public string? FindHostExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "agent", "CSIViewer.Host.Agent.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CSIViewer Host", "agent", "CSIViewer.Host.Agent.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<string> InstallAsync(CancellationToken cancellationToken = default)
    {
        LastOperationSucceeded = false;
        var exe = FindHostExecutable();
        if (exe is null)
            return Localization.Format("Falta el agente de arranque: {0}. Repara la instalación de CSIViewer Host.",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CSIViewer Host", "agent", "CSIViewer.Host.Agent.exe"));
        try { ValidateAgent(exe); }
        catch (InstallationValidationException ex)
        {
            var reason = ex.Issue switch
            {
                InstallationIssue.Missing => Localization.Text("No existe el archivo o la carpeta."),
                InstallationIssue.OutsideProgramFiles => Localization.Text("El agente debe estar instalado en Program Files."),
                InstallationIssue.RedirectedPath => Localization.Text("La ruta contiene un enlace o una redirección."),
                InstallationIssue.UntrustedOwner => Localization.Text("El propietario no es SYSTEM, Administradores ni TrustedInstaller."),
                InstallationIssue.UnsafePermissions => Localization.Text("Una cuenta no administrativa puede modificar la instalación."),
                _ => Localization.Text("Windows no permite leer los permisos de esta ruta.")
            };
            return Localization.Format("No se puede activar el inicio. Ruta: {0}. Motivo: {1}", ex.ItemPath, reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Localization.Format("No se pueden comprobar los permisos de {0}. No se ha modificado el inicio automático.", exe);
        }
        var body = $$"""
            $hostExe = '{{Escape(exe)}}'
            Assert-HostInstallation $hostExe
            $existing = Get-TaskObject
            if ($null -ne $existing) {
                if ($existing.Definition.Actions.Count -ne 1 -or $existing.Definition.Actions.Item(1).Path -ne $hostExe) { throw 'Unexpected task action' }
            }
            $legacy = Get-Service -Name '{{TaskName}}' -ErrorAction SilentlyContinue
            if ($null -ne $legacy) {
                Stop-Service -Name '{{TaskName}}' -Force -ErrorAction Stop
                sc.exe delete '{{TaskName}}' | Out-Null
                if ($LASTEXITCODE -notin @(0,1072)) { throw 'Legacy removal failed' }
            }
            $action = New-ScheduledTaskAction -Execute $hostExe -Argument '--scheduled'
            $trigger = New-ScheduledTaskTrigger -AtStartup
            $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName '{{TaskName}}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
            Set-TaskReadAccess (Get-TaskObject)
            Start-ScheduledTask -TaskName '{{TaskName}}'
            $verified = Get-TaskObject
            if ($null -eq $verified -or -not $verified.Enabled -or $verified.Definition.Actions.Item(1).Path -ne $hostExe) { throw 'Activation not confirmed' }
            """;
        return await PerformAsync(body, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> UninstallAsync(CancellationToken cancellationToken = default)
    {
        LastOperationSucceeded = false;
        var body = $$"""
            $existing = Get-TaskObject
            if ($null -ne $existing) {
                if ($existing.Definition.Actions.Count -ne 1 -or [IO.Path]::GetFileName($existing.Definition.Actions.Item(1).Path) -ne 'CSIViewer.Host.Agent.exe') { throw 'Unexpected task action' }
                if ([int]$existing.State -eq 4) { Stop-ScheduledTask -TaskName '{{TaskName}}' -ErrorAction Stop }
                Unregister-ScheduledTask -TaskName '{{TaskName}}' -Confirm:$false -ErrorAction Stop
            }
            $legacy = Get-Service -Name '{{TaskName}}' -ErrorAction SilentlyContinue
            if ($null -ne $legacy) {
                Stop-Service -Name '{{TaskName}}' -Force -ErrorAction Stop
                sc.exe delete '{{TaskName}}' | Out-Null
                if ($LASTEXITCODE -notin @(0,1072)) { throw 'Legacy removal failed' }
            }
            if ($null -ne (Get-TaskObject)) { throw 'Deactivation not confirmed' }
            """;
        return await PerformAsync(body, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PerformAsync(string body, bool installed, CancellationToken token)
    {
        var code = await RunElevatedAsync(body, token).ConfigureAwait(false);
        var status = Decode(code);
        LastOperationSucceeded = status.State != HostStartupState.Unknown && status.IsInstalled == installed &&
            (!installed || status.State != HostStartupState.Disabled);
        if (status.State != HostStartupState.Unknown) _confirmed = status;
        if (LastOperationSucceeded) return Localization.Text("Operación completada.");
        if (code == 1223) return Localization.Text("Permiso de administrador cancelado; no se ha cambiado nada.");
        return Localization.Format("La operación no se ha confirmado. Código {0}. No se mostrará como activada sin verificar la tarea.", code);
    }

    private static HostStartupStatus Decode(int code) => new(code switch
    {
        20 => HostStartupState.NotInstalled, 21 => HostStartupState.Disabled,
        22 or 23 => HostStartupState.Ready, 24 => HostStartupState.Running, _ => HostStartupState.Unknown
    });

    private static async Task<int> RunElevatedAsync(string body, CancellationToken token)
    {
        // Status is returned through the process exit code. No elevated writes
        // to user-controlled temporary result files or localized-text parsing.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            function Get-TaskObject {
                $service = New-Object -ComObject 'Schedule.Service'
                $service.Connect()
                try { return $service.GetFolder('\').GetTask('{{TaskName}}') }
                catch { if (($_.Exception.GetBaseException().HResult -band 65535) -in @(2,3)) { return $null }; throw }
            }
            function Set-TaskReadAccess($task) {
                $task.SetSecurityDescriptor('D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)', 0)
                $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($task.GetSecurityDescriptor(4))
                $readConfirmed = $false
                foreach ($ace in $descriptor.DiscretionaryAcl) {
                    if ($ace -is [Security.AccessControl.CommonAce] -and $ace.AceQualifier -eq 'AccessAllowed' -and $ace.SecurityIdentifier.Value -eq 'S-1-5-32-545') {
                        if ($ace.AccessMask -ne 0x120089) { throw 'Unexpected task query permissions' }
                        $readConfirmed = $true
                    }
                }
                if (-not $readConfirmed) { throw 'Task query permission not confirmed' }
            }
            function Assert-ProtectedItem([IO.FileSystemInfo]$item) {
                if (-not $item.Exists -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Missing or redirected installation path' }
                $acl = Get-Acl -LiteralPath $item.FullName
                $trusted = @('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
                if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) { throw 'Untrusted installation owner' }
                $write = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteAttributes,WriteExtendedAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
                foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
                    if ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
                    if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and ($rule.FileSystemRights -band $write)) { throw 'Unsafe installation permissions' }
                }
            }
            function Assert-HostInstallation([string]$exe) {
                $anchor = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')
                if (-not [IO.Path]::GetFullPath($exe).StartsWith($anchor+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Agent outside Program Files' }
                $file = Get-Item -LiteralPath $exe
                Assert-ProtectedItem $file
                for ($dir = $file.Directory; $null -ne $dir; $dir = $dir.Parent) {
                    Assert-ProtectedItem $dir
                    if ($dir.FullName -eq $anchor) { break }
                }
                $pending = New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
                $pending.Push($file.Directory)
                while ($pending.Count -gt 0) {
                    $dir = $pending.Pop()
                    Assert-ProtectedItem $dir
                    foreach ($item in $dir.EnumerateFileSystemInfos()) {
                        Assert-ProtectedItem $item
                        if ($item -is [IO.DirectoryInfo]) { $pending.Push($item) }
                    }
                }
            }
            try {
                {{body}}
                $task = Get-TaskObject
                if ($null -eq $task) { exit 20 }
                if (-not $task.Enabled) { exit 21 }
                switch ([int]$task.State) { 2 { exit 22 }; 3 { exit 23 }; 4 { exit 24 }; default { exit 90 } }
            } catch { exit 90 }
            """;
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-NoProfile -EncodedCommand {encoded}", UseShellExecute = true,
                Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return 90;
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return 1223; }
        catch { return 90; }
    }

    private static void ValidateAgent(string path)
    {
        ProtectedInstallation.ValidateProgramFilesParents(path);
        ProtectedInstallation.ValidateItem(new FileInfo(path));
        ProtectedInstallation.ValidateTree(Path.GetDirectoryName(path)!);
    }

    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);
}
