param(
    [ValidatePattern('^[A-Za-z0-9_-]{1,60}$')][string]$RunName = 'vmware02',
    [string]$InstalledDirectory = (Join-Path $env:ProgramFiles 'CSIViewer Host'),
    [string]$InstalledDataDirectory = (Join-Path $env:LOCALAPPDATA 'CSIViewer.Host'),
    [switch]$SkipProcessInspection
)
$ErrorActionPreference = 'Stop'
$data = Join-Path $PSScriptRoot "runs/Host-$RunName"
if (-not (Test-Path (Join-Path $data '.csiviewer-diagnostic-data'))) {
    throw 'Ejecuta esta copia junto al manifiesto del paquete; no existe ese RunName de diagnóstico.'
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'build-manifest.json') -Raw | ConvertFrom-Json
function Read-VideoSettings([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @{Status='Missing';Values=@{}} }
    $inputFile = $null; $reader = $null
    try {
        $inputFile = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        if ($inputFile.Length -gt 1MB) { return @{Status='TooLarge';Values=@{}} }
        $reader = [IO.StreamReader]::new($inputFile)
        $settings = $reader.ReadToEnd() | ConvertFrom-Json
        $values = [ordered]@{}
        foreach ($name in @('Fps','ImageQuality','MaxWidth','UploadLimitKbps')) {
            $value = $settings.$name
            if ($null -eq $value) { $values[$name] = $null }
            elseif (($value -is [int] -or $value -is [long]) -and $value -ge 0 -and $value -le [int]::MaxValue) {
                $values[$name] = $value
            } else { $values[$name] = 'InvalidTypeOrRange' }
        }
        foreach ($name in @('PreferModernCapture','PreferH264','PreferDirectLan')) {
            $value = $settings.$name
            $values[$name] = if ($null -eq $value) { $null } elseif ($value -is [bool]) { $value } else { 'InvalidType' }
        }
        return @{Status='Read';Values=$values;PersistedOnly=$true}
    } catch { return @{Status='Unreadable';Values=@{}} }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        elseif ($null -ne $inputFile) { $inputFile.Dispose() }
    }
}
$binaries = @(
    foreach ($origin in @(@{Role='Installed';Root=$InstalledDirectory}, @{Role='Portable';Root=(Join-Path $PSScriptRoot 'host')})) {
        foreach ($name in @('CSIViewer.Host.exe','CSIViewer.Host.dll','CSIViewer.Media.dll','CSIViewer.Services.dll')) {
            $path = Join-Path $origin.Root $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                [ordered]@{Origin=$origin.Role;File=$name;Status='Missing'}
                continue
            }
            try {
                $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
                [ordered]@{Origin=$origin.Role;File=$name;Status='Read';SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;
                    FileVersion=$version.FileVersion;ProductVersion=$version.ProductVersion}
            } catch { [ordered]@{Origin=$origin.Role;File=$name;Status='Unreadable'} }
        }
    }
)
$processes = @()
$processStatus = 'Skipped'
if (-not $SkipProcessInspection) {
    try {
        $processes = @(Get-CimInstance Win32_Process -Filter "Name='CSIViewer.Host.exe' OR Name='CSIViewer.Host.Agent.exe'" |
            ForEach-Object {
                $origin = 'Unknown'
                if ($_.ExecutablePath) {
                    if ($_.ExecutablePath.StartsWith((Join-Path $PSScriptRoot 'host\'), [StringComparison]::OrdinalIgnoreCase)) { $origin='Portable' }
                    elseif ($_.ExecutablePath.StartsWith($InstalledDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { $origin='Installed' }
                }
                [ordered]@{Name=$_.Name;Pid=$_.ProcessId;ParentPid=$_.ParentProcessId;Origin=$origin;
                    CommandLineAvailable=($null -ne $_.CommandLine);
                    UnattendedFlag=($_.CommandLine -match '(^|\s)--unattended(\s|$)');
                    DataDirectoryFlag=($_.CommandLine -match '(^|\s)--data-?dir(\s|=)')}
            })
        $processStatus = 'Read'
    } catch { $processStatus='Unavailable' }
}
$report = [ordered]@{
    Type='host-comparison';BuildId=$manifest.BuildId;Utc=[DateTime]::UtcNow.ToString('o');RunName=$RunName;
    InstalledVideoSettings=(Read-VideoSettings (Join-Path $InstalledDataDirectory 'settings.json'));
    PortableVideoSettings=(Read-VideoSettings (Join-Path $data 'settings.json'));
    Binaries=$binaries;ProcessInspection=$processStatus;Processes=$processes;
    Notes='Read-only inspection. Only numeric/boolean video preferences; no server, password, identity, enrollment, DB, raw settings or full command lines. Persisted settings need not equal an active negotiated stream. ProductVersion revision is not a guarantee of clean source.'
}
$name = 'host-comparison-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.json'
$output = Join-Path $data $name
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
Write-Host "Comparación guardada: $output"
Write-Host 'No se ha cerrado, iniciado ni configurado ningún proceso. Copia este JSON al ordenador del visor.'
