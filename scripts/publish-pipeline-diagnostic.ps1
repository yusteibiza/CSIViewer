param([ValidatePattern('^[A-Za-z0-9_-]{1,80}$')][string]$BuildId = ('diag-' + (Get-Date -AsUTC -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = Join-Path $root "artifacts/diagnostics/$BuildId"
if (Test-Path $output) { throw 'No se sobrescribe una compilación de diagnóstico existente.' }
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($target in @(@('CSIViewer','viewer'), @('CSIViewer.Host','host'))) {
    dotnet publish (Join-Path $root "src/$($target[0])/$($target[0]).csproj") -c Release -r win-x64 --self-contained true `
        --artifacts-path (Join-Path $root "artifacts/diagnostic-build-$BuildId") `
        "-p:DiagnosticBuildId=$BuildId" -o (Join-Path $output $target[1]) --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($target[0])" }
}
Copy-Item (Join-Path $PSScriptRoot 'start-pipeline-diagnostic.ps1') (Join-Path $output 'Start-Diagnostic.ps1')
Copy-Item (Join-Path $PSScriptRoot 'collect-pipeline-diagnostic.ps1') (Join-Path $output 'Collect-Diagnostic.ps1')
Copy-Item (Join-Path $PSScriptRoot 'compare-host-diagnostic.ps1') (Join-Path $output 'Compare-Host.ps1')
Copy-Item (Join-Path $PSScriptRoot 'start-diagnostic-host.cmd') (Join-Path $output 'Start-Host.cmd')
Copy-Item (Join-Path $PSScriptRoot 'start-diagnostic-viewer.cmd') (Join-Path $output 'Start-Viewer.cmd')
Copy-Item (Join-Path $root 'docs/47-diagnostico-vmware.md') (Join-Path $output 'LEEME.md')
$files = @(Get-ChildItem $output -Recurse -File | ForEach-Object {
    if ($_.Extension -in @('.pfx','.p12','.db','.sqlite','.key') -or $_.Name -in @('settings.json','identity.json')) {
        throw "Unexpected personal/secret material in publish: $($_.Name)"
    }
    [ordered]@{Path=[IO.Path]::GetRelativePath($output,$_.FullName).Replace('\','/');Length=$_.Length;SHA256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{BuildId=$BuildId;Version='1.0.1.0';Configuration='Release';Runtime='win-x64';SelfContained=$true;
    CreatedUtc=[DateTime]::UtcNow.ToString('o');Files=$files;Purpose='Diagnostic portable only; not release-approved; no installer/agent/service provisioning.'
}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'build-manifest.json') -Encoding utf8
$zip = $output + '.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
Get-FileHash $zip -Algorithm SHA256
