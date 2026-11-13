param(
    [string]$Version = "1.0.1",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$installers = Join-Path $root "installers"
$output = Join-Path $root "artifacts\installers"
$payload = Join-Path $output "payload"

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc)
{
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command)
    {
        $iscc = $command.Source
    }
}

if (-not $iscc)
{
    throw "No se encontró Inno Setup 6 (ISCC.exe). Instálalo con: winget install JRSoftware.InnoSetup"
}

function Publish-Project
{
    param(
        [string]$Project,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    dotnet publish (Join-Path $root $Project) `
        -c $Configuration -r win-x64 --self-contained true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $Destination
    if ($LASTEXITCODE -ne 0)
    {
        throw "Falló la publicación de $Project."
    }
}

if (-not $SkipPublish)
{
    Write-Host "Limpiando payload anterior..." -ForegroundColor Cyan
    Remove-Item -Recurse -Force $payload -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $payload | Out-Null

    Write-Host "Publicando CSIViewer..." -ForegroundColor Cyan
    Publish-Project "src\CSIViewer\CSIViewer.csproj" (Join-Path $payload "client")

    Write-Host "Publicando CSIViewer Servidor..." -ForegroundColor Cyan
    Publish-Project "src\CSIViewer.Server\CSIViewer.Server.csproj" (Join-Path $payload "server")

    Write-Host "Publicando CSIViewer Host (agente de inicio)..." -ForegroundColor Cyan
    Publish-Project "src\CSIViewer.Host.Agent\CSIViewer.Host.Agent.csproj" (Join-Path $payload "host\agent")

    Write-Host "Publicando CSIViewer Host (ventana)..." -ForegroundColor Cyan
    Publish-Project "src\CSIViewer.Host\CSIViewer.Host.csproj" (Join-Path $payload "host\ui")
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

$scripts = @("client.iss", "server.iss", "host.iss")
foreach ($script in $scripts)
{
    Write-Host "Compilando $script..." -ForegroundColor Cyan
    & $iscc "/Qp" "/DMyVersion=$Version" (Join-Path $installers $script)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Falló la compilación de $script."
    }
}

$installersBuilt = Get-ChildItem $output -Filter "*-Setup-*.exe" |
    Sort-Object Name
if ($installersBuilt.Count -eq 0)
{
    throw "No se generó ningún instalador."
}

Write-Host ""
Write-Host "Instaladores generados:" -ForegroundColor Green
foreach ($installer in $installersBuilt)
{
    Write-Host ("  {0} ({1:N1} MB)" -f $installer.FullName, ($installer.Length / 1MB))
}
