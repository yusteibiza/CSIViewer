param(
    [string[]]$LogDirectory,
    [DateTimeOffset]$Since = [DateTimeOffset]::Now.AddMinutes(-20),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\diagnostics')
)

# Read-only collection of mouse metadata. Never export raw messages,
# exceptions, chat text, keystrokes, settings, credentials or database files.
$ErrorActionPreference = 'Stop'
$actions = @('move', 'leftDown', 'leftUp', 'rightDown', 'rightUp', 'middleDown', 'middleUp', 'wheel', 'hwheel', 'pressed', 'released')
$stages = @('Input UI event.', 'Input UI mapped.', 'Input transport result.',
    'Input received.', 'Input rejected.', 'Input injection result.',
    'Input decrypt rejected', 'Input dropped before send.', 'Input send failed.')
$fields = @('TraceId', 'SessionId', 'Action', 'X', 'Y', 'HasPosition',
    'Width', 'Height', 'FrameWidth', 'FrameHeight', 'CanControl', 'Mapped',
    'NX', 'NY', 'Source', 'Written', 'Success', 'Monitor', 'Diagnostics',
    'ControlOwner', 'Permissions', 'SessionState', 'Found')

if (-not $LogDirectory) {
    $LogDirectory = @(
        (Join-Path $env:LOCALAPPDATA 'CSIViewer\logs'),
        (Join-Path $env:LOCALAPPDATA 'CSIViewer.Host\logs')
    )
    if ($env:CSIVIEWER_DATA_DIR) {
        $LogDirectory += Join-Path $env:CSIVIEWER_DATA_DIR 'logs'
    }
    foreach ($key in @('HKCU:\Software\CSIViewer\Client', 'HKLM:\Software\CSIViewer\Client')) {
        $installed = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($installed -and $installed.InstallPath) {
            $LogDirectory += Join-Path $installed.InstallPath 'data\logs'
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path $OutputDirectory ('mouse-trace-{0}-{1}.jsonl' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8))
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
$writer = New-Object IO.StreamWriter($stream, (New-Object Text.UTF8Encoding($false)))
$count = 0
try {
    foreach ($directory in ($LogDirectory | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $directory -File -Filter '*.log')) {
            $inputStream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = New-Object IO.StreamReader($inputStream)
            try {
                while ($null -ne ($line = $reader.ReadLine())) {
                    try { $entry = $line | ConvertFrom-Json } catch { continue }
                    if (-not $entry.ts -or ([DateTimeOffset]$entry.ts) -lt $Since) { continue }
                    $stage = $null
                    foreach ($candidate in $stages) {
                        if ($entry.message -and $entry.message.StartsWith($candidate, [StringComparison]::Ordinal)) {
                            $stage = $candidate
                            break
                        }
                    }
                    if (-not $stage) { continue }
                    $properties = $entry.properties
                    if ($properties.Action -and $properties.Action -notin $actions) { continue }
                    $row = [ordered]@{ ts = $entry.ts; stage = $stage; pid = $entry.pid; tid = $entry.tid }
                    foreach ($field in $fields) {
                        $property = $properties.PSObject.Properties[$field]
                        if ($null -ne $property) { $row[$field] = $property.Value }
                    }
                    $writer.WriteLine(($row | ConvertTo-Json -Compress -Depth 5))
                    $count++
                }
            }
            finally { $reader.Dispose() }
        }
    }
}
finally { $writer.Dispose() }

Write-Output "Mouse events exported: $count"
Write-Output "Output: $output"
if ($count -eq 0) {
    Write-Warning 'No matching traces. Check the log directory, time range and that both programs include the new instrumentation. This does not prove input events were lost.'
}
