param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root "src/CSIViewer.Server/bin/$Configuration/net10.0-windows10.0.19041.0/win-x64/CSIViewerServidor.exe"
$client = Join-Path $root "src/CSIViewer/bin/$Configuration/net10.0-windows10.0.19041.0/win-x64/CSIViewer.exe"

if (-not (Test-Path $server) -or -not (Test-Path $client)) {
    dotnet build (Join-Path $root "CSIViewer.slnx") -c $Configuration
}

$serverProcess = Start-Process -FilePath $server -WorkingDirectory (Split-Path $server) -PassThru
Start-Sleep -Seconds 2

$env:CSIVIEWER_ALLOW_MULTI = "1"
$env:CSIVIEWER_DATA_DIR = "$env:TEMP\CSIViewer\ClientA"
$processA = Start-Process -FilePath $client -PassThru

$env:CSIVIEWER_DATA_DIR = "$env:TEMP\CSIViewer\ClientB"
$processB = Start-Process -FilePath $client -PassThru

Write-Host "Servidor PID: $($serverProcess.Id)"
Write-Host "Cliente A PID: $($processA.Id)  datos: $env:TEMP\CSIViewer\ClientA"
Write-Host "Cliente B PID: $($processB.Id)  datos: $env:TEMP\CSIViewer\ClientB"
Write-Host "Cierra las ventanas y el servidor cuando termines."
