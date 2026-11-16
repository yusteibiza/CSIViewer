param([Parameter(Mandatory=$true)][string]$Package)
$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path $Package).Path
& (Join-Path $packagePath 'Start-Diagnostic.ps1') -Role Viewer -VerifyOnly
& (Join-Path $packagePath 'Start-Diagnostic.ps1') -Role Host -VerifyOnly
$manifest = Get-Content (Join-Path $packagePath 'build-manifest.json') -Raw | ConvertFrom-Json
foreach ($relative in @('viewer/CSIViewer.dll','host/CSIViewer.Host.dll','viewer/CSIViewer.Services.dll','host/CSIViewer.Services.dll')) {
    $inputFile = [IO.File]::OpenRead((Join-Path $packagePath $relative))
    $pe = [Reflection.PortableExecutable.PEReader]::new($inputFile)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $found = $false
        foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            $blob = $metadata.GetBlobReader($attribute.Value)
            try {
                if ($blob.ReadUInt16() -eq 1 -and $blob.ReadSerializedString() -eq 'CSIViewerDiagnosticBuild') {
                    if ($blob.ReadSerializedString() -ne $manifest.BuildId) { throw 'BuildId mismatch' }
                    $found = $true
                }
            } catch [BadImageFormatException] { }
        }
        if (-not $found) { throw "Missing embedded build identity: $relative" }
        Write-Host "Embedded identity verified: $relative / $($manifest.BuildId)"
    } finally { $pe.Dispose(); $inputFile.Dispose() }
}
# Only synthetic log collection. Never launch viewer/host or touch installed data.
$fixture = Join-Path $PSScriptRoot ('../artifacts/audit/package-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'collect-pipeline-diagnostic.ps1') (Join-Path $fixture 'Collect-Diagnostic.ps1')
Copy-Item (Join-Path $packagePath 'build-manifest.json') $fixture
$data = Join-Path $fixture 'runs/Host-synthetic'
$logs = Join-Path $data 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
'synthetic' | Set-Content (Join-Path $data '.csiviewer-diagnostic-data')
'{"Type":"sample","Synthetic":true}' | Set-Content (Join-Path $logs 'pipeline-synthetic.jsonl')
'synthetic normal log' | Set-Content (Join-Path $logs 'csiviewer-synthetic.log')
'synthetic host log' | Set-Content (Join-Path $logs 'csiviewer.host-synthetic.log')
'synthetic rotated host log' | Set-Content (Join-Path $logs 'csiviewer.host-synthetic.log.1')
'DO NOT COLLECT' | Set-Content (Join-Path $logs 'csiviewer-private.log.settings.json')
'DO NOT COLLECT' | Set-Content (Join-Path $logs 'settings.json')
'DO NOT COLLECT' | Set-Content (Join-Path $data 'identity.json')
& (Join-Path $fixture 'Collect-Diagnostic.ps1') -Role Host -RunName synthetic
$zip = Get-ChildItem (Join-Path $fixture 'runs') -Filter *.zip
$archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
try {
    $names = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @('pipeline-synthetic.jsonl','csiviewer-synthetic.log','csiviewer.host-synthetic.log','csiviewer.host-synthetic.log.1','build-manifest.json','collection.txt')) {
        if ($names -notcontains $required) { throw "Missing collected evidence: $required" }
    }
    if ($names.Count -ne 6) { throw "Unexpected collected content: $($names -join ',')" }
} finally { $archive.Dispose() }
Write-Host "Package integrity and synthetic collection passed. Evidence: $fixture"
