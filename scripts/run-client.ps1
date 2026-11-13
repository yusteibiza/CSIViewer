param(
    [string]$DataDirectory = "$env:LOCALAPPDATA\CSIViewer",
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($DataDirectory) {
    $env:CSIVIEWER_DATA_DIR = $DataDirectory
}
dotnet build (Join-Path $root "src/CSIViewer/CSIViewer.csproj") -c $Configuration
$exe = Join-Path $root "src/CSIViewer/bin/$Configuration/net10.0-windows10.0.19041.0/win-x64/CSIViewer.exe"
if (-not (Test-Path $exe)) {
    throw "No se encontró el ejecutable: $exe"
}
& $exe
