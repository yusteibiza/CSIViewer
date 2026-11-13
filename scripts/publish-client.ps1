param(
    [string]$Output = "artifacts/client"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root $Output
New-Item -ItemType Directory -Force -Path $target | Out-Null

dotnet publish (Join-Path $root "src/CSIViewer/CSIViewer.csproj") `
    -c Release -r win-x64 --self-contained true -o $target

dotnet publish (Join-Path $root "src/CSIViewer.Host/CSIViewer.Host.csproj") `
    -c Release -r win-x64 --self-contained true -o $target

dotnet publish (Join-Path $root "src/CSIViewer.Host.Agent/CSIViewer.Host.Agent.csproj") `
    -c Release -r win-x64 --self-contained true -o (Join-Path $target "agent")

Write-Host "Visor y host publicados en $target"
Write-Host "Instala el modo desatendido con scripts/install-host-service.ps1 como administrador."
