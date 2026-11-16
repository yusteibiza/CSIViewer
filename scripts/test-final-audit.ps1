param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$isolated = Join-Path $env:LOCALAPPDATA ('Temp/opencode/csiviewer-final-audit-' + [Guid]::NewGuid().ToString('N'))
$results = Join-Path $root 'artifacts/audit/tests'
$names = @('TEMP','TMP','CSIVIEWER_DATA_DIR','CSIVIEWER_SERVER_DATA_DIR','CSIVIEWER_SERVER_DB','CSIVIEWER_PIPELINE_DIAGNOSTICS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
New-Item -ItemType Directory -Force -Path $isolated, $results | Out-Null
try {
    $env:CSIVIEWER_PIPELINE_DIAGNOSTICS = '0'
    $env:TEMP = $isolated
    $env:TMP = $isolated
    $env:CSIVIEWER_DATA_DIR = Join-Path $isolated 'client-data'
    $env:CSIVIEWER_SERVER_DATA_DIR = Join-Path $isolated 'server-data'
    $env:CSIVIEWER_SERVER_DB = Join-Path $isolated 'server.db'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    [ordered]@{ Started = (Get-Date).ToString('o'); IsolatedRoot = $isolated;
        Excluded = 'StartupServiceTests.SetEnabled_WritesAndRemovesRunEntry';
        Reason = 'TEMP and data directories do not isolate HKCU Run. Requires disposable Windows profile.'
    } | ConvertTo-Json | Set-Content (Join-Path $root "artifacts/audit/context-$stamp.json") -Encoding utf8
    Push-Location $root
    try {
        dotnet restore CSIViewer.slnx --artifacts-path artifacts/audit-build --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
        dotnet build CSIViewer.slnx -c $Configuration --artifacts-path artifacts/audit-build --no-restore --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
        dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c $Configuration --artifacts-path artifacts/audit-build --no-build --no-restore --nologo --filter 'FullyQualifiedName!~SetEnabled_WritesAndRemovesRunEntry' --logger "trx;LogFileName=final-audit-$stamp.trx" --results-directory $results
        if ($LASTEXITCODE -ne 0) { throw 'Regression failed; inspect TRX' }
    } finally { Pop-Location }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
# Evidence is intentionally retained. No HKCU, task, service, installed app,
# certificate, installer or user's database is changed by this script.
