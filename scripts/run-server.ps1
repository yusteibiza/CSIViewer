param(
    [string]$Configuration = "Debug",
    [ValidateSet("ui", "servidor")]
    [string]$Mode = "ui",
    [int]$Port = 0,
    [switch]$AllInterfaces,
    [ValidateSet("system", "light", "dark")]
    [string]$Theme = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root "src/CSIViewer.Server/CSIViewer.Server.csproj") -c $Configuration

$serverArgs = @("--modo=$Mode")
if ($Port -gt 0) {
    $serverArgs += "--port=$Port"
}
if ($AllInterfaces) {
    $serverArgs += "--all-interfaces"
}
if ($Theme) {
    $serverArgs += "--theme=$Theme"
}

dotnet run --project (Join-Path $root "src/CSIViewer.Server/CSIViewer.Server.csproj") `
    -c $Configuration --no-build -- @serverArgs
