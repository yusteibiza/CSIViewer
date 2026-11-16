param(
    [Parameter(Mandatory=$true)][ValidateSet('Viewer','Host')][string]$Role,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_-]{1,60}$')][string]$RunName
)
$ErrorActionPreference = 'Stop'
$data = Join-Path $PSScriptRoot "runs/$Role-$RunName"
if (-not (Test-Path (Join-Path $data '.csiviewer-diagnostic-data'))) { throw 'No existe ese diagnóstico dentro de runs.' }
$logs = Join-Path $data 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$request = [IO.File]::Open((Join-Path $logs 'pipeline-dump.request'), [IO.FileMode]::Create,
    [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
try { $request.WriteByte(1) } finally { $request.Dispose() }
Start-Sleep -Seconds 3
Add-Type -AssemblyName System.IO.Compression
$zip = Join-Path $PSScriptRoot ("runs/$Role-$RunName-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
$file = [IO.File]::Open($zip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create)
try {
    $sources = @(Get-ChildItem $logs -File | Where-Object {
        $_.Name -match '^(pipeline-.+\.jsonl(\.\d+)?|csiviewer(\.host)?-.+\.log(\.\d+)?)$' -and
        -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
    })
    $pipelineCount = @($sources | Where-Object Name -like 'pipeline-*.jsonl*').Count
    if ($pipelineCount -eq 0) { Write-Warning 'No hay registros pipeline en esta ejecución. El ZIP no permite diagnosticar las etapas de vídeo de este extremo.' }
    $sources += Get-Item (Join-Path $PSScriptRoot 'build-manifest.json')
    foreach ($source in $sources) {
        $entry = $archive.CreateEntry($source.Name, [IO.Compression.CompressionLevel]::Optimal)
        $destination = $entry.Open()
        $input = [IO.File]::Open($source.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try { $input.CopyTo($destination) } finally { $input.Dispose(); $destination.Dispose() }
    }
    $entry = $archive.CreateEntry('collection.txt')
    $writer = [IO.StreamWriter]::new($entry.Open())
    try {
        $writer.WriteLine("Role=$Role Run=$RunName UTC=$([DateTime]::UtcNow.ToString('o')) OS=$([Environment]::OSVersion)")
        $writer.WriteLine("PipelineFiles=$pipelineCount")
        $writer.WriteLine('Live collection: the last JSONL line can be incomplete. No settings/database/identity files included.')
    } finally { $writer.Dispose() }
} finally { $archive.Dispose(); $file.Dispose() }
Write-Host "Registros: $zip"
Write-Host 'Revisa datos personales de los logs existentes antes de compartir. Conserva ambos ZIP, del host y del visor.'
