param(
    [string]$ServiceExe = "",
    [string]$ClientExe = "",
    [switch]$Uninstall,
    [switch]$RepairReadAccess
)

$ErrorActionPreference = "Stop"
$taskName = "CSIViewerHost"
$legacyServiceName = "CSIViewerHost"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecuta este script como administrador."
}

function Find-Executable([string]$explicitPath, [string]$fileName, [string]$projectName) {
    if ($explicitPath) {
        if (-not (Test-Path $explicitPath)) {
            throw "No existe: $explicitPath"
        }
        return (Resolve-Path $explicitPath).Path
    }

    $root = Split-Path -Parent $PSScriptRoot
    $direct = Join-Path $root "artifacts/host/$fileName"
    if (Test-Path $direct) {
        return (Resolve-Path $direct).Path
    }

    $candidate = Get-ChildItem (Join-Path $root "src/$projectName/bin") -Recurse -Filter $fileName -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($candidate) {
        return $candidate.FullName
    }

    throw "No se encontró $fileName. Compila el proyecto o indica la ruta."
}

function Remove-LegacyService {
    Stop-Service -Name $legacyServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $legacyServiceName 2>$null | Out-Null
}

function Set-TaskReadAccess($task) {
    # Users can QUERY only. Execution, writes, deletion and ACL changes remain
    # restricted to SYSTEM/Administrators. Do not change the root task folder.
    $task.SetSecurityDescriptor('D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)', 0)
    $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new($task.GetSecurityDescriptor(4))
    $readConfirmed = $false
    foreach ($ace in $descriptor.DiscretionaryAcl) {
        if ($ace -is [Security.AccessControl.CommonAce] -and $ace.AceQualifier -eq 'AccessAllowed' -and $ace.SecurityIdentifier.Value -eq 'S-1-5-32-545') {
            if ($ace.AccessMask -ne 0x120089) { throw 'Permisos de consulta de tarea inesperados.' }
            $readConfirmed = $true
        }
    }
    if (-not $readConfirmed) { throw 'No se ha confirmado el permiso de lectura del estado.' }
}

if ($Uninstall) {
    & schtasks.exe /Delete /TN $taskName /F 2>$null | Out-Null
    Remove-LegacyService
    Write-Host "Inicio automático de CSIViewer Host eliminado."
    return
}

$hostExe = Find-Executable $ServiceExe "CSIViewer.Host.Agent.exe" "CSIViewer.Host.Agent"

# Validate protected binaries before registering any SYSTEM task. TrustedInstaller
# is a Windows principal normally inherited from Program Files, not a user account.
function Assert-ProtectedItem([IO.FileSystemInfo]$item) {
    if (-not $item.Exists -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Ruta de instalación ausente o redirigida: $($item.FullName)"
    }
    $acl = Get-Acl -LiteralPath $item.FullName
    $trusted = @('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
        throw "Propietario de instalación no seguro: $($item.FullName)"
    }
    $write = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteAttributes,WriteExtendedAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and ($rule.FileSystemRights -band $write)) {
            throw "Una cuenta no administrativa puede modificar: $($item.FullName)"
        }
    }
}

$anchor = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')
if (-not [IO.Path]::GetFullPath($hostExe).StartsWith($anchor+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'El agente debe estar instalado para todos los usuarios en Program Files.'
}
$file = Get-Item -LiteralPath $hostExe
if ($file.Name -ne 'CSIViewer.Host.Agent.exe') { throw 'Ejecutable de agente inesperado.' }
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

$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$taskFolder = $scheduler.GetFolder('\')
if ($RepairReadAccess) {
    try { $existing = $taskFolder.GetTask($taskName) }
    catch {
        if ($_.Exception.GetBaseException().HResult -in @(-2147024894,-2147024893)) { return }
        throw
    }
    if ($existing.Definition.Actions.Count -ne 1 -or $existing.Definition.Actions.Item(1).Path -ne $hostExe -or
        $existing.Definition.Principal.UserId -notin @('SYSTEM','S-1-5-18','NT AUTHORITY\SYSTEM')) {
        throw 'La tarea existente no corresponde al agente SYSTEM esperado.'
    }
    Set-TaskReadAccess $existing
    Write-Host 'Lectura del estado reparada; no se ha iniciado, detenido ni reconfigurado el agente.'
    return
}

# Remove the old Windows service if it was installed by a previous version.
Remove-LegacyService

$action = New-ScheduledTaskAction -Execute $hostExe -Argument '--scheduled'
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings -Description 'Inicia CSIViewer Host al arrancar Windows' -Force | Out-Null
Set-TaskReadAccess ($taskFolder.GetTask($taskName))

Start-ScheduledTask -TaskName $taskName
$verified = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
if (-not $verified.Settings.Enabled -or $verified.Actions.Count -ne 1 -or $verified.Actions[0].Execute -ne $hostExe) {
    throw 'No se ha podido confirmar la tarea de inicio automático.'
}
Write-Host "Tarea programada $taskName creada y iniciada."
Write-Host "Host: $hostExe"
Write-Host "Se ejecutará como SYSTEM al arrancar Windows, sin UAC en cada reinicio."
