param(
    [string]$ServiceExe = "",
    [string]$ClientExe = "",
    [switch]$Uninstall
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

if ($Uninstall) {
    & schtasks.exe /Delete /TN $taskName /F 2>$null | Out-Null
    Remove-LegacyService
    Write-Host "Inicio automático de CSIViewer Host eliminado."
    return
}

$hostExe = Find-Executable $ServiceExe "CSIViewer.Host.Agent.exe" "CSIViewer.Host.Agent"

# Remove the old Windows service if it was installed by a previous version.
Remove-LegacyService

$action = New-ScheduledTaskAction -Execute $hostExe -Argument '--scheduled'
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings -Description 'Inicia CSIViewer Host al arrancar Windows' -Force | Out-Null

Start-ScheduledTask -TaskName $taskName
Write-Host "Tarea programada $taskName creada y iniciada."
Write-Host "Host: $hostExe"
Write-Host "Se ejecutará como SYSTEM al arrancar Windows, sin UAC en cada reinicio."
