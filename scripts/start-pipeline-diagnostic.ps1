param(
    [Parameter(Mandatory=$true)][ValidateSet('Viewer','Host')][string]$Role,
    [ValidatePattern('^[A-Za-z0-9_-]{1,60}$')][string]$RunName = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'build-manifest.json'
if (-not (Test-Path $manifestPath)) { throw 'Ejecuta la copia Start-Diagnostic.ps1 del paquete de diagnóstico extraído.' }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$processName = if ($Role -eq 'Host') { 'CSIViewer.Host' } else { 'CSIViewer' }
if (-not $VerifyOnly -and (Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
    throw "Ya existe $processName. Cierra esa aplicación desde su interfaz antes de iniciar el diagnóstico; este script no la detendrá."
}
$relative = if ($Role -eq 'Host') { 'host/CSIViewer.Host.exe' } else { 'viewer/CSIViewer.exe' }
$exe = Join-Path $PSScriptRoot $relative
$prefix = if ($Role -eq 'Host') { 'host/' } else { 'viewer/' }
$expected = @($manifest.Files | Where-Object { $_.Path.StartsWith($prefix, [StringComparison]::Ordinal) })
if (-not ($expected | Where-Object { $_.Path -eq $relative })) { throw 'El manifiesto no incluye el ejecutable.' }
foreach ($item in $expected) {
    if ($item.Path.Contains('..') -or [IO.Path]::IsPathRooted($item.Path)) { throw 'Ruta inválida en manifiesto.' }
    $path = Join-Path $PSScriptRoot $item.Path
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $item.SHA256) { throw "Hash incorrecto: $($item.Path)" }
}
$actual = @(Get-ChildItem (Join-Path $PSScriptRoot $prefix) -Recurse -File)
if ($actual.Count -ne $expected.Count) { throw 'El directorio contiene archivos no incluidos en el manifiesto.' }
if ($VerifyOnly) { Write-Host "Integridad verificada: $Role / $($manifest.BuildId) / $($expected.Count) archivos. No se inicia ningún proceso."; return }
$data = Join-Path $PSScriptRoot "runs/$Role-$RunName"
$marker = Join-Path $data '.csiviewer-diagnostic-data'
if ((Test-Path $data) -and -not (Test-Path $marker)) { throw 'La carpeta ya existe y no está identificada como datos de este diagnóstico.' }
New-Item -ItemType Directory -Path $data -Force | Out-Null
$manifest.BuildId | Set-Content $marker -Encoding utf8
$previousData = $env:CSIVIEWER_DATA_DIR
$previousTrace = $env:CSIVIEWER_PIPELINE_DIAGNOSTICS
try {
    $env:CSIVIEWER_DATA_DIR = $data
    $env:CSIVIEWER_PIPELINE_DIAGNOSTICS = '1'
    # The host explicitly selects LocalAppData unless --data-dir is supplied;
    # an environment override alone does not isolate that executable.
    $start = @{ FilePath = $exe; WorkingDirectory = (Split-Path $exe); PassThru = $true }
    if ($Role -eq 'Host') { $start.ArgumentList = @('--data-dir', ('"{0}"' -f $data)) }
    $process = Start-Process @start
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $ready = $false
    do {
        if ($process.HasExited) {
            throw 'El proceso lanzado terminó. Puede seguir activa otra instancia del host. No se ha detenido ningún proceso.'
        }
        $logs = Join-Path $data 'logs'
        if (Test-Path $logs) {
            $ready = @(Get-ChildItem $logs -Filter "pipeline-$($process.Id)-*.jsonl" -File |
                Where-Object Length -gt 0).Count -gt 0
        }
        if (-not $ready) { Start-Sleep -Milliseconds 250 }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) {
        throw "No se ha confirmado el diagnóstico del proceso $($process.Id). No lo des por iniciado. No se ha cerrado nada. Revisa: $data"
    }
    Write-Host "Build: $($manifest.BuildId)"
    Write-Host "Proceso: $($process.Id). Datos/logs: $data"
    Write-Host "Para recoger: .\Collect-Diagnostic.ps1 -Role $Role -RunName $RunName"
} finally {
    $env:CSIVIEWER_DATA_DIR = $previousData
    $env:CSIVIEWER_PIPELINE_DIAGNOSTICS = $previousTrace
}
