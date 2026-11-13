param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root "src/CSIViewer/CSIViewer.csproj") `
    -c $Configuration -p:PackageMsix=true -p:Platform=x64

$package = Get-ChildItem (Join-Path $root "src/CSIViewer/AppPackages") -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($package) {
    Write-Host "MSIX generado: $($package.FullName)"
    Write-Host "Paquete sin firmar: instálalo en modo desarrollador o fírmalo con un certificado de confianza."
} else {
    Write-Error "No se encontró el MSIX generado."
}
