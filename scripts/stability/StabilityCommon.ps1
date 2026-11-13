# Utilidades compartidas por la batería de estabilidad de CSIViewer.
# No contiene lógica de producto: solo orquestación de procesos y métricas.

$ErrorActionPreference = "Stop"

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$script:ServerExe = Join-Path $script:RepoRoot "src\CSIViewer.Server\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CSIViewerServidor.exe"
$script:HarnessExe = Join-Path $script:RepoRoot "tools\CSIViewer.TestHarness\bin\Debug\net10.0-windows10.0.19041.0\CSIViewer.TestHarness.exe"
$script:ClientExe = Join-Path $script:RepoRoot "src\CSIViewer\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CSIViewer.exe"

$global:StabilityResults = [System.Collections.Generic.List[object]]::new()
$global:StabilityProcesses = [System.Collections.Generic.List[object]]::new()
$global:StabilityOutputRoot = ""

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StabilityNative
{
    [DllImport("ntdll.dll")]
    public static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    public static extern int NtResumeProcess(IntPtr processHandle);
}
'@

function Write-Section([string]$Text)
{
    Write-Host ""
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function New-ScenarioDir([string]$Name)
{
    $dir = Join-Path $global:StabilityOutputRoot $Name
    if (Test-Path $dir)
    {
        Remove-Item -Recurse -Force $dir
    }

    New-Item -ItemType Directory -Force $dir | Out-Null
    return $dir
}

function Get-FreeTcpPort([int]$Start = 5180)
{
    for ($port = $Start; $port -lt ($Start + 120); $port++)
    {
        $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        if (-not $listener)
        {
            return $port
        }
    }

    throw "No hay puertos libres para la batería de estabilidad."
}

function Wait-ForFileLine
{
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        if (Test-Path $Path)
        {
            $match = Select-String -Path $Path -Pattern $Pattern -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($match)
            {
                return $match.Line
            }
        }

        Start-Sleep -Milliseconds 250
    }

    return $null
}

function Wait-ForFileCount
{
    param(
        [string]$Path,
        [string]$Pattern,
        [int]$Count,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        if (Test-Path $Path)
        {
            $matches = @(Select-String -Path $Path -Pattern $Pattern -ErrorAction SilentlyContinue)
            if ($matches.Count -ge $Count)
            {
                return $matches.Count
            }
        }

        Start-Sleep -Milliseconds 250
    }

    if (Test-Path $Path)
    {
        return @(Select-String -Path $Path -Pattern $Pattern -ErrorAction SilentlyContinue).Count
    }

    return 0
}

function Get-FileText([string]$Path)
{
    if (Test-Path $Path)
    {
        return Get-Content -Raw $Path
    }

    return ""
}

function Get-HarnessText($Harness)
{
    return (Get-FileText $Harness.Output) + "`n" + (Get-FileText $Harness.ErrorLog)
}

function Wait-ProcessExit
{
    param($Process, [int]$TimeoutSeconds = 30)

    if ($null -eq $Process)
    {
        return $true
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $Process.HasExited -and (Get-Date) -lt $deadline)
    {
        Start-Sleep -Milliseconds 200
        $Process.Refresh()
    }

    return $Process.HasExited
}

function Stop-TrackedProcess
{
    param($Process)

    if ($null -eq $Process)
    {
        return
    }

    try
    {
        $Process.Refresh()
        if (-not $Process.HasExited)
        {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch
    {
        # El proceso ya terminó.
    }
}

function Stop-SuiteProcesses
{
    foreach ($process in @($global:StabilityProcesses))
    {
        Stop-TrackedProcess $process
    }

    $global:StabilityProcesses.Clear()
    Get-Process CSIViewer.TestHarness -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Write-HarnessSettings
{
    param(
        [string]$DataDir,
        [int]$Port,
        [hashtable]$Overrides = @{}
    )

    New-Item -ItemType Directory -Force $DataDir | Out-Null
    $settings = [ordered]@{
        schemaVersion = 1
        deviceName = "Harness-" + (Split-Path $DataDir -Leaf)
        serverUrl = "http://localhost:$Port"
        enrollmentCode = "DEV-1234"
        autoConnect = $true
        reconnectMaxRetries = 30
        reconnectInitialDelaySeconds = 1
        reconnectMaxDelaySeconds = 5
        reconnectJitterPercent = 0
        heartbeatIntervalSeconds = 5
        heartbeatTimeoutSeconds = 20
        temporaryPasswordLifetimeMinutes = 60
        notificationsEnabled = $false
        askBeforeReceivingFiles = $false
        downloadFolder = Join-Path $DataDir "downloads"
    }

    foreach ($key in $Overrides.Keys)
    {
        $settings[$key] = $Overrides[$key]
    }

    $settings | ConvertTo-Json -Depth 6 |
        Set-Content -Path (Join-Path $DataDir "settings.json") -Encoding utf8
}

function Start-TestServer
{
    param(
        [string]$ScenarioDir,
        [int]$Port,
        [hashtable]$ExtraArguments = @{}
    )

    $db = Join-Path $ScenarioDir "server.db"
    $log = Join-Path $ScenarioDir "server.log"
    $err = Join-Path $ScenarioDir "server.err.log"
    $arguments = @(
        "--modo=servidor",
        "--port=$Port",
        "--db=$db",
        "--enrollment-code=DEV-1234",
        "--max-sessions-per-host=10",
        "--no-upnp",
        "--no-stun",
        "--local-only"
    )

    foreach ($key in $ExtraArguments.Keys)
    {
        $arguments += "--$key=$($ExtraArguments[$key])"
    }

    $process = Start-Process -FilePath $script:ServerExe -ArgumentList $arguments `
        -RedirectStandardOutput $log -RedirectStandardError $err `
        -PassThru -WindowStyle Hidden
    $global:StabilityProcesses.Add($process)

    if (-not (Wait-ForFileLine -Path $log -Pattern "SERVER_READY" -TimeoutSeconds 40))
    {
        Stop-TrackedProcess $process
        $details = Get-FileText $err
        throw "El servidor no arrancó en el puerto $Port. $details"
    }

    return [pscustomobject]@{
        Process = $process
        Log = $log
        ErrorLog = $err
        Database = $db
        Port = $Port
    }
}

function Start-Harness
{
    param(
        [string]$ScenarioDir,
        [string]$Name,
        [string]$Mode,
        [int]$Port,
        [string]$HostId = "",
        [string]$Password = "",
        [hashtable]$Overrides = @{},
        [hashtable]$Environment = @{}
    )

    $dataDir = Join-Path $ScenarioDir "data-$Name"
    Write-HarnessSettings -DataDir $dataDir -Port $Port -Overrides $Overrides
    $out = Join-Path $ScenarioDir "$Name.log"
    $err = Join-Path $ScenarioDir "$Name.err.log"
    $environmentVariables = @{
        PATH = $env:PATH
        CSIVIEWER_HARNESS_DATA = $dataDir
        CSIVIEWER_HARNESS_STATS = "1"
        CSIVIEWER_HARNESS_FORCE_GC = "1"
    }

    foreach ($key in $Environment.Keys)
    {
        $environmentVariables[$key] = [string]$Environment[$key]
    }

    $arguments = @($Mode)
    if ($HostId) { $arguments += $HostId }
    if ($Password) { $arguments += $Password }

    $process = Start-Process -FilePath $script:HarnessExe -ArgumentList $arguments `
        -Environment $environmentVariables `
        -RedirectStandardOutput $out -RedirectStandardError $err `
        -PassThru -WindowStyle Hidden
    $global:StabilityProcesses.Add($process)

    return [pscustomobject]@{
        Process = $process
        Output = $out
        ErrorLog = $err
        DataDir = $dataDir
        Name = $Name
    }
}

function Start-HostHarness
{
    param(
        [string]$ScenarioDir,
        [int]$Port,
        [int]$DurationSeconds = 120,
        [hashtable]$Overrides = @{},
        [hashtable]$Environment = @{}
    )

    $env = @{ CSIVIEWER_HARNESS_DURATION = [string]$DurationSeconds }
    foreach ($key in $Environment.Keys)
    {
        $env[$key] = $Environment[$key]
    }

    $hostHarness = Start-Harness -ScenarioDir $ScenarioDir -Name "host" -Mode "host" -Port $Port `
        -Overrides $Overrides -Environment $env
    $line = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_READY" -TimeoutSeconds 45
    if (-not $line)
    {
        throw "El host no quedó listo. $(Get-FileText $hostHarness.ErrorLog)"
    }

    $id = [regex]::Match($line, "id=(\S+)").Groups[1].Value
    $password = [regex]::Match($line, "password=(\S+)").Groups[1].Value
    if (-not $id -or -not $password)
    {
        throw "No se pudo leer el ID o la contraseña del host: $line"
    }

    $hostHarness | Add-Member -NotePropertyName HostId -NotePropertyValue $id
    $hostHarness | Add-Member -NotePropertyName Password -NotePropertyValue $password
    return $hostHarness
}

function Get-Metrics
{
    param($Process)

    if ($null -eq $Process)
    {
        return $null
    }

    try
    {
        $Process.Refresh()
        if ($Process.HasExited)
        {
            return $null
        }

        $tcp = @(Get-NetTCPConnection -OwningProcess $Process.Id -ErrorAction SilentlyContinue)
        $udp = @(Get-NetUDPEndpoint -OwningProcess $Process.Id -ErrorAction SilentlyContinue)
        return [pscustomobject]@{
            Pid = $Process.Id
            WorkingSetMB = [math]::Round($Process.WorkingSet64 / 1MB, 1)
            PrivateMB = [math]::Round($Process.PrivateMemorySize64 / 1MB, 1)
            Handles = $Process.HandleCount
            Threads = $Process.Threads.Count
            Tcp = $tcp.Count
            Established = @($tcp | Where-Object { $_.State -eq "Established" }).Count
            Listen = @($tcp | Where-Object { $_.State -eq "Listen" }).Count
            Udp = $udp.Count
        }
    }
    catch
    {
        return $null
    }
}

function Format-Metrics($Metrics)
{
    if ($null -eq $Metrics)
    {
        return "proceso terminado"
    }

    return ("ws={0}MB private={1}MB handles={2} threads={3} tcp={4} established={5} listen={6} udp={7}" -f `
        $Metrics.WorkingSetMB, $Metrics.PrivateMB, $Metrics.Handles, $Metrics.Threads,
        $Metrics.Tcp, $Metrics.Established, $Metrics.Listen, $Metrics.Udp)
}

function Convert-StatLine([string]$Line)
{
    $values = @{}
    if (-not $Line)
    {
        return $values
    }

    foreach ($pair in (($Line -replace "^STATS\s+\S+\s+", "") -split "\s+"))
    {
        $match = [regex]::Match($pair, "^([A-Za-z0-9_]+)=(-?\d+)$")
        if ($match.Success)
        {
            $values[$match.Groups[1].Value] = [double]$match.Groups[2].Value
        }
    }

    return $values
}

function Get-StatLine([string[]]$Lines, [string]$Tag)
{
    $pattern = "^STATS\s+" + [regex]::Escape($Tag) + "\s+(.+)$"
    $line = $Lines | Where-Object { $_ -match $pattern } | Select-Object -Last 1
    if (-not $line)
    {
        return $null
    }

    return Convert-StatLine $line
}

function Get-StatValue([string[]]$Lines, [string]$Tag, [string]$Key)
{
    $values = Get-StatLine -Lines $Lines -Tag $Tag
    if ($null -eq $values -or -not $values.ContainsKey($Key))
    {
        return $null
    }

    return $values[$Key]
}

function Assert-Condition
{
    param([bool]$Condition, [string]$Message)

    if (-not $Condition)
    {
        throw "ASSERT: $Message"
    }
}

function Add-Result
{
    param(
        [int]$Id,
        [string]$Name,
        [ValidateSet("Ejecutada", "Simulada", "Pendiente")]
        [string]$Type,
        [ValidateSet("PASS", "FAIL", "PENDING")]
        [string]$Result,
        [string]$Evidence,
        [string]$Details = ""
    )

    $global:StabilityResults.Add([pscustomobject]@{
        Id = $Id
        Name = $Name
        Type = $Type
        Result = $Result
        Evidence = $Evidence
        Details = $Details
        Timestamp = (Get-Date).ToString("O")
    })

    $color = switch ($Result)
    {
        "PASS" { "Green" }
        "FAIL" { "Red" }
        default { "Yellow" }
    }

    Write-Host ("[{0}] {1:D2} {2} ({3}) - {4}" -f `
        $Result, $Id, $Name, $Type, $Evidence) -ForegroundColor $color
}

function Invoke-Scenario
{
    param(
        [int]$Id,
        [string]$Name,
        [scriptblock]$Block
    )

    Write-Section "[$Id] $Name"
    try
    {
        & $Block
    }
    catch
    {
        Add-Result -Id $Id -Name $Name -Type "Ejecutada" -Result "FAIL" `
            -Evidence $_.Exception.Message -Details $_.ScriptStackTrace
    }
    finally
    {
        Stop-SuiteProcesses
        Start-Sleep -Milliseconds 500
    }
}

function Suspend-TrackedProcess($Process)
{
    $Process.Refresh()
    if ($Process.HasExited)
    {
        throw "No se puede suspender un proceso terminado."
    }

    $code = [StabilityNative]::NtSuspendProcess($Process.Handle)
    if ($code -ne 0)
    {
        throw "NtSuspendProcess devolvió $code."
    }
}

function Resume-TrackedProcess($Process)
{
    $Process.Refresh()
    if ($Process.HasExited)
    {
        return
    }

    $code = [StabilityNative]::NtResumeProcess($Process.Handle)
    if ($code -ne 0)
    {
        throw "NtResumeProcess devolvió $code."
    }
}

function Test-ServerHealth([int]$Port)
{
    try
    {
        $response = Invoke-WebRequest -Uri "http://localhost:$Port/api/health" `
            -TimeoutSec 3 -UseBasicParsing
        return $response.StatusCode -eq 200
    }
    catch
    {
        return $false
    }
}
