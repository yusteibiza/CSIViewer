param(
    [string]$ServiceExe = "",
    [int]$Port = 5080,
    [string]$EnrollmentCode = "DEV-1234",
    [switch]$AllInterfaces,
    [switch]$Upnp,
    [switch]$Https,
    [string]$CertificateThumbprint = "",
    [switch]$Stun,
    [int]$StunPort = 3478,
    [string]$DatabasePath = "$env:ProgramData\CSIViewerServidor\server.db",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$serviceName = "CSIViewerServidor"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecuta este script como administrador."
}

if ($Uninstall) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName
    Write-Host "Servicio $serviceName eliminado."
    return
}

# The service creates the database as SYSTEM. Grant the interactive user
# write access so the server UI can also use the same database.
$prepareScript = Join-Path $PSScriptRoot "prepare-server-data.ps1"
if (Test-Path $prepareScript) {
    & $prepareScript -DataDirectory (Split-Path -Parent $DatabasePath)
}

if (-not $ServiceExe) {
    $root = Split-Path -Parent $PSScriptRoot
    $published = Join-Path $root "artifacts/server/CSIViewerServidor.exe"
    if (Test-Path $published) {
        $ServiceExe = (Resolve-Path $published).Path
    } else {
        $candidate = Get-ChildItem (Join-Path $root "src/CSIViewer.Server/bin") -Recurse -Filter "CSIViewerServidor.exe" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $candidate) {
            throw "No se encontró CSIViewerServidor.exe. Publica el servidor o indica -ServiceExe."
        }
        $ServiceExe = $candidate.FullName
    }
}

$arguments = @("`"$ServiceExe`"", "--modo=servicio", "--port=$Port", "--db=`"$DatabasePath`"")
if ($EnrollmentCode) {
    $arguments += "--enrollment-code=`"$EnrollmentCode`""
}
if ($AllInterfaces) {
    $arguments += "--all-interfaces"
}
if ($Upnp) {
    $arguments += "--upnp"
}
if ($Https -or $CertificateThumbprint) {
    $arguments += "--https"
}
if ($CertificateThumbprint) {
    $arguments += "--cert-thumbprint=`"$CertificateThumbprint`""
}
if ($Stun) {
    $arguments += "--stun"
    $arguments += "--stun-port=$StunPort"
}

$binPath = $arguments -join " "

Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
& sc.exe delete $serviceName 2>$null | Out-Null
Start-Sleep -Milliseconds 500

New-Service -Name $serviceName `
    -BinaryPathName $binPath `
    -StartupType Automatic `
    -DisplayName "CSIViewer Servidor" `
    -Description "Servidor de senalizacion y relay de CSIViewer"

& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
Start-Service $serviceName
Write-Host "Servicio $serviceName instalado y iniciado."
Write-Host "Puerto: $Port"
Write-Host "Base de datos: $DatabasePath"
