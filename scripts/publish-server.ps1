param(
    [string]$Output = "artifacts/server"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet publish (Join-Path $root "src/CSIViewer.Server/CSIViewer.Server.csproj") `
    -c Release -r win-x64 --self-contained true -o (Join-Path $root $Output)
Write-Host "Servidor publicado en $(Join-Path $root $Output)"
