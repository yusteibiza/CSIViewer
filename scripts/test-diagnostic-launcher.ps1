$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root ('artifacts/audit/launcher-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'host') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'start-pipeline-diagnostic.ps1') (Join-Path $fixture 'Start-Diagnostic.ps1')
$exe = Join-Path $fixture 'host/CSIViewer.Host.exe'
'synthetic, never executed' | Set-Content $exe
@{BuildId='synthetic';Files=@(@{Path='host/CSIViewer.Host.exe';SHA256=(Get-FileHash $exe -Algorithm SHA256).Hash})} |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture 'build-manifest.json')
$beforeData = $env:CSIVIEWER_DATA_DIR
$beforeTrace = $env:CSIVIEWER_PIPELINE_DIAGNOSTICS
$probe = @{ Launched = $false }
# Scoped mocks: never start an application or interact with real processes.
function Get-Process { param($Name) }
function Start-Process {
    param($FilePath, $WorkingDirectory, [switch]$PassThru, $ArgumentList)
    if ($ArgumentList.Count -ne 2 -or $ArgumentList[0] -ne '--data-dir' -or
        $ArgumentList[1] -ne ('"{0}"' -f $env:CSIVIEWER_DATA_DIR)) { throw 'Missing explicit host data argument' }
    if ($env:CSIVIEWER_PIPELINE_DIAGNOSTICS -ne '1') { throw 'Diagnostics not enabled' }
    $logs = Join-Path $env:CSIVIEWER_DATA_DIR 'logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    '{"Type":"identity","Synthetic":true}' | Set-Content (Join-Path $logs 'pipeline-4242-synthetic.jsonl')
    $probe.Launched = $true
    [pscustomobject]@{Id=4242;HasExited=$false}
}
& (Join-Path $fixture 'Start-Diagnostic.ps1') -Role Host -RunName synthetic
if (-not $probe.Launched) { throw 'Mock not reached' }
if ($env:CSIVIEWER_DATA_DIR -ne $beforeData -or $env:CSIVIEWER_PIPELINE_DIAGNOSTICS -ne $beforeTrace) {
    throw 'Parent environment not restored'
}
$probe.Launched = $false
& (Join-Path $fixture 'Start-Diagnostic.ps1') -Role Host -VerifyOnly
if ($probe.Launched) { throw 'VerifyOnly launched a process' }
function Get-Process { param($Name) [pscustomobject]@{Id=1} }
$refused = $false
try { & (Join-Path $fixture 'Start-Diagnostic.ps1') -Role Host -RunName occupied }
catch { $refused = $_.Exception.Message -like 'Ya existe*' }
if (-not $refused -or $probe.Launched) { throw 'Existing process not safely refused' }
function Get-Process { param($Name) }
function Start-Process {
    param($FilePath, $WorkingDirectory, [switch]$PassThru, $ArgumentList)
    [pscustomobject]@{Id=4243;HasExited=$true}
}
$refused = $false
try { & (Join-Path $fixture 'Start-Diagnostic.ps1') -Role Host -RunName exited }
catch { $refused = $_.Exception.Message -like 'El proceso lanzado terminó*' }
if (-not $refused) { throw 'Early exit incorrectly reported as success' }
if ($env:CSIVIEWER_DATA_DIR -ne $beforeData -or $env:CSIVIEWER_PIPELINE_DIAGNOSTICS -ne $beforeTrace) {
    throw 'Failure path did not restore environment'
}
Write-Host "PASS: host --data-dir quoting, diagnostic readiness, environment restore, VerifyOnly, existing-process refusal and early-exit detection. Synthetic only: $fixture"
