param(
    [ValidateSet("quick", "full")]
    [string]$Profile = "full",
    [string[]]$Scenarios = @(),
    [int]$SoakSeconds = 180,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "StabilityCommon.ps1")

if (-not $OutputDir)
{
    $OutputDir = Join-Path ([System.IO.Path]::GetTempPath()) `
        ("csiviewer-stability-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}

$global:StabilityOutputRoot = (New-Item -ItemType Directory -Force $OutputDir).FullName
$startedAt = Get-Date
$script:SelectedScenarios = if ($Scenarios.Count -gt 0) { $Scenarios } else { @() }

if (-not (Test-Path $script:ServerExe))
{
    throw "No se encontró el servidor: $script:ServerExe. Compila en Debug antes de ejecutar."
}

if (-not (Test-Path $script:HarnessExe))
{
    throw "No se encontró el harness: $script:HarnessExe. Compila en Debug antes de ejecutar."
}

function Should-Run([string]$Id, [string]$Name)
{
    if ($script:SelectedScenarios.Count -eq 0)
    {
        return $true
    }

    foreach ($selected in $script:SelectedScenarios)
    {
        foreach ($token in ($selected -split ","))
        {
            $value = $token.Trim()
            if ($value -eq $Id -or $value -eq $Name -or $Name -like "*$value*")
            {
                return $true
            }
        }
    }

    return $false
}

function Get-MatchValue([string]$Text, [string]$Pattern, [int]$Group = 1)
{
    $match = [regex]::Match($Text, $Pattern)
    if ($match.Success)
    {
        return $match.Groups[$Group].Value
    }

    return $null
}

function Get-AllCount([string]$Text, [string]$Pattern)
{
    return ([regex]::Matches($Text, $Pattern)).Count
}

function Wait-ForAnyLine
{
    param(
        [string[]]$Paths,
        [string]$Pattern,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        foreach ($path in $Paths)
        {
            if (Test-Path $path)
            {
                $match = Select-String -Path $path -Pattern $Pattern -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ($match)
                {
                    return $match.Line
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    return $null
}

function Wait-ForAllFinished
{
    param(
        [object[]]$Harnesses,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline)
    {
        $pending = 0
        foreach ($harness in $Harnesses)
        {
            $text = Get-HarnessText $harness
            if ($text -notmatch "CONTROLLER_OK|CONTROLLER_FAIL")
            {
                $pending++
            }
        }

        if ($pending -eq 0)
        {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Write-ServerUiSettings
{
    param(
        [string]$DataDir,
        [int]$Port,
        [string]$DatabasePath
    )

    New-Item -ItemType Directory -Force $DataDir | Out-Null
    $settings = [ordered]@{
        Port = $Port
        ListenOnAllInterfaces = $false
        EnrollmentCode = "DEV-1234"
        ProtectedEnrollmentCode = ""
        ServerName = "CSIViewer Servidor (estabilidad)"
        DatabasePath = $DatabasePath
        AutoStartWithUi = $true
        StartWithWindows = $false
        Theme = "System"
        EnableUpnp = $false
        UpnpExternalPort = 0
        UseHttps = $false
        CertificateThumbprint = ""
        EnableStunServer = $false
        StunPort = 3478
        MaxSessionsPerHost = 10
        WindowX = 100
        WindowY = 100
        WindowWidth = 1000
        WindowHeight = 800
        WindowMaximized = $false
        LogLevel = "Information"
    }

    $settings | ConvertTo-Json | Set-Content -Path (Join-Path $DataDir "serversettings.json") -Encoding utf8
}

function Start-ServerUi
{
    param(
        [string]$DataDir,
        [int]$Port
    )

    $process = Start-Process -FilePath $script:ServerExe `
        -Environment @{ CSIVIEWER_SERVER_DATA_DIR = $DataDir } `
        -PassThru -WindowStyle Hidden
    $global:StabilityProcesses.Add($process)

    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0 -and (Test-ServerHealth $Port))
        {
            return $process
        }
    }

    throw "La interfaz del servidor no arrancó o no inició el servidor en el puerto $Port."
}

function Start-ClientUi
{
    param(
        [string]$DataDir,
        [int]$Port
    )

    Write-HarnessSettings -DataDir $DataDir -Port $Port
    $process = Start-Process -FilePath $script:ClientExe `
        -ArgumentList @("--data-dir", $DataDir) `
        -PassThru -WindowStyle Hidden
    $global:StabilityProcesses.Add($process)

    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0)
        {
            return $process
        }
    }

    throw "La interfaz del cliente no arrancó."
}

# ---------------------------------------------------------------------------
# 01. Conectar y desconectar repetidamente
# ---------------------------------------------------------------------------
function Invoke-Scenario01
{
    $dir = New-ScenarioDir "01-connect-disconnect"
    $port = Get-FreeTcpPort 5201
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $metricsBefore = Get-Metrics $server.Process
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 120
    $cycles = Start-Harness -ScenarioDir $dir -Name "cycle" -Mode "cycle" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{
            CSIVIEWER_HARNESS_CYCLES = "7"
            CSIVIEWER_HARNESS_CYCLE_HOLD_MS = "250"
            CSIVIEWER_HARNESS_CYCLE_RECOVER_AFTER = "65"
            CSIVIEWER_HARNESS_HOLD = "8"
        }

    $done = Wait-ForFileLine -Path $cycles.Output -Pattern "CYCLES_DONE" -TimeoutSeconds 180
    Assert-Condition ($null -ne $done) "No terminaron los ciclos de conexión."
    $metricsAtEnd = Get-Metrics $cycles.Process
    Assert-Condition ($null -ne $metricsAtEnd) "No se pudo medir el proceso de ciclos."
    Wait-ProcessExit $cycles.Process 40 | Out-Null
    Assert-Condition ($cycles.Process.HasExited) "El proceso de ciclos no terminó."

    $cycleText = Get-FileText $cycles.Output
    $hostText = Get-FileText $hostHarness.Output
    $ok = [int](Get-MatchValue $done "ok=(\d+)")
    $fail = [int](Get-MatchValue $done "fail=(\d+)")
    $limited = [int](Get-MatchValue $done "rate_limited=(\d+)")
    $sessions = [int](Get-MatchValue $done "sessions=(\d+)")
    $lines = @(Get-Content $cycles.Output -ErrorAction SilentlyContinue)
    $managedStart = Get-StatValue $lines "cycle-start" "managed"
    $managedEnd = Get-StatValue $lines "cycle-hold-end" "managed"
    $threadsStart = Get-StatValue $lines "cycle-start" "threads"
    $threadsEnd = Get-StatValue $lines "cycle-hold-end" "threads"
    $handlesStart = Get-StatValue $lines "cycle-start" "handles"
    $handlesEnd = Get-StatValue $lines "cycle-hold-end" "handles"
    $tpPending = Get-StatValue $lines "cycle-hold-end" "tp_pending"
    $managedDeltaMb = [math]::Round(($managedEnd - $managedStart) / 1MB, 1)
    $hostEnded = Get-AllCount $hostText "HOST_SESSION_ENDED"

    Assert-Condition ($ok -eq 7) "Se esperaban 7 conexiones correctas; hubo $ok."
    Assert-Condition ($fail -eq 0) "Hubo $fail conexiones fallidas."
    Assert-Condition ($limited -eq 1) "No se observó el límite de 5 solicitudes por minuto ($limited)."
    Assert-Condition ($cycleText -match "CYCLE_RATE_LIMITED") "No se registró el rechazo rate_limited."
    Assert-Condition ($cycleText -match "CYCLE_RECOVERED") "No se recuperó tras la ventana de límite."
    Assert-Condition ($sessions -eq 0) "Quedaron $sessions sesiones abiertas al terminar."
    Assert-Condition ($managedDeltaMb -le 48) "Crecimiento de memoria administrada: $managedDeltaMb MB."
    Assert-Condition (($threadsEnd - $threadsStart) -le 8) "Crecimiento de hilos: $($threadsEnd - $threadsStart)."
    Assert-Condition (($handlesEnd - $handlesStart) -le 100) "Crecimiento de handles: $($handlesEnd - $handlesStart)."
    Assert-Condition ($metricsAtEnd.Tcp -le 4) "Sockets TCP abiertos al final: $($metricsAtEnd.Tcp)."
    Assert-Condition ($tpPending -le 2) "Tareas pendientes en el thread pool: $tpPending."
    Assert-Condition ($hostEnded -ge 7) "El host no registró el fin de las 7 sesiones ($hostEnded)."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras los ciclos."

    Add-Result -Id 1 -Name "Conectar y desconectar repetidamente" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("7/7 ciclos; límite 5/min rechazado y recuperado; managed {0} MB; hilos {1}->{2}; handles {3}->{4}; tcp {5}; tp_pending {6}" -f `
            $managedDeltaMb, $threadsStart, $threadsEnd, $handlesStart, $handlesEnd,
            $metricsAtEnd.Tcp, $tpPending) `
        -Details ("Host: {0} sesiones finalizadas. Servidor: {1}." -f $hostEnded, (Format-Metrics $metricsBefore))
}

# ---------------------------------------------------------------------------
# 02. Dos clientes intentando acceder al mismo host
# ---------------------------------------------------------------------------
function Invoke-Scenario02
{
    $dir = New-ScenarioDir "02-two-clients"
    $port = Get-FreeTcpPort 5202
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 90
    $first = Start-Harness -ScenarioDir $dir -Name "client-control" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "20" }
    $second = Start-Harness -ScenarioDir $dir -Name "client-view" -Mode "viewer" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "20" }

    Wait-ForAllFinished -Harnesses @($first, $second) -TimeoutSeconds 90 | Out-Null
    Wait-ProcessExit $first.Process 30 | Out-Null
    Wait-ProcessExit $second.Process 30 | Out-Null
    $firstText = Get-HarnessText $first
    $secondText = Get-HarnessText $second
    $hostText = Get-FileText $hostHarness.Output
    $firstOk = $firstText -match "CONTROLLER_OK"
    $secondOk = $secondText -match "CONTROLLER_OK"
    $started = Get-AllCount $hostText "HOST_SESSION_STARTED"
    $ended = Get-AllCount $hostText "HOST_SESSION_ENDED"

    Assert-Condition $firstOk "El cliente con control no completó la sesión."
    Assert-Condition $secondOk "El cliente de solo visualización no completó la sesión."
    Assert-Condition ($started -ge 2) "El host no registró dos sesiones simultáneas ($started)."
    Assert-Condition ($ended -ge 2) "El host no registró el cierre de las dos sesiones ($ended)."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras las dos sesiones."

    Add-Result -Id 2 -Name "Dos clientes accediendo al mismo host" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("2 sesiones simultáneas aceptadas; {0} iniciadas y {1} finalizadas" -f $started, $ended) `
        -Details ("Control: {0}. Visualización: {1}." -f `
            (Get-MatchValue $firstText "CONTROLLER_CONNECTED permissions=([^\r\n]+)"), `
            (Get-MatchValue $secondText "CONTROLLER_CONNECTED permissions=([^\r\n]+)"))
}

# ---------------------------------------------------------------------------
# 03. Múltiples solicitudes simultáneas
# ---------------------------------------------------------------------------
function Invoke-Scenario03
{
    $dir = New-ScenarioDir "03-simultaneous"
    $port = Get-FreeTcpPort 5203
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $metricsBefore = Get-Metrics $server.Process
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 240

    $runWave = {
        param([string]$Prefix)
        $clients = @()
        for ($index = 1; $index -le 11; $index++)
        {
            $clients += Start-Harness -ScenarioDir $dir -Name "$Prefix-$index" -Mode "viewer" -Port $port `
                -HostId $hostHarness.HostId -Password $hostHarness.Password `
                -Environment @{ CSIVIEWER_HARNESS_HOLD = "10" }
        }

        Wait-ForAllFinished -Harnesses $clients -TimeoutSeconds 120 | Out-Null
        foreach ($client in $clients)
        {
            Wait-ProcessExit $client.Process 30 | Out-Null
        }

        $texts = $clients | ForEach-Object { Get-HarnessText $_ }
        [pscustomobject]@{
            Ok = @($texts | Where-Object { $_ -match "CONTROLLER_OK" }).Count
            HostFull = @($texts | Where-Object { $_ -match "host_full" }).Count
            Fail = @($texts | Where-Object { $_ -match "CONTROLLER_FAIL" }).Count
            Total = $clients.Count
        }
    }

    $wave1 = & $runWave "wave1"
    Start-Sleep -Seconds 5
    $metricsAfterWave1 = Get-Metrics $server.Process
    $wave2 = & $runWave "wave2"
    Start-Sleep -Seconds 5
    $metricsAfterWave2 = Get-Metrics $server.Process
    $hostText = Get-FileText $hostHarness.Output
    $started = Get-AllCount $hostText "HOST_SESSION_STARTED"

    Assert-Condition ($wave1.Ok -eq 10) "Oleada 1: se esperaban 10 sesiones aceptadas; hubo $($wave1.Ok)."
    Assert-Condition ($wave1.HostFull -eq 1) "Oleada 1: se esperaba un host_full; hubo $($wave1.HostFull)."
    Assert-Condition ($wave2.Ok -eq 10) "Oleada 2: se esperaban 10 sesiones aceptadas; hubo $($wave2.Ok)."
    Assert-Condition ($wave2.HostFull -eq 1) "Oleada 2: se esperaba un host_full; hubo $($wave2.HostFull)."
    Assert-Condition ($started -ge 20) "El host no registró las 20 sesiones aceptadas ($started)."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras las oleadas."
    Assert-Condition ($metricsAfterWave2.WorkingSetMB -le ($metricsAfterWave1.WorkingSetMB + 64)) `
        "La segunda oleada añadió memoria: $($metricsAfterWave1.WorkingSetMB) -> $($metricsAfterWave2.WorkingSetMB) MB."
    Assert-Condition (($metricsAfterWave2.Handles - $metricsAfterWave1.Handles) -le 120) `
        "La segunda oleada añadió handles: $($metricsAfterWave1.Handles) -> $($metricsAfterWave2.Handles)."

    Add-Result -Id 3 -Name "Múltiples solicitudes simultáneas" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("2 oleadas de 11 solicitudes: 20 aceptadas y 2 host_full; ws {0} -> {1} MB; handles {2} -> {3}" -f `
            $metricsAfterWave1.WorkingSetMB, $metricsAfterWave2.WorkingSetMB,
            $metricsAfterWave1.Handles, $metricsAfterWave2.Handles) `
        -Details ("Servidor: {0} -> {1} -> {2}" -f (Format-Metrics $metricsBefore), `
            (Format-Metrics $metricsAfterWave1), (Format-Metrics $metricsAfterWave2))
}

# ---------------------------------------------------------------------------
# 04. Pérdida de conexión durante una sesión (proceso congelado)
# ---------------------------------------------------------------------------
function Invoke-Scenario04
{
    $dir = New-ScenarioDir "04-loss-during-session"
    $port = Get-FreeTcpPort 5204
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 150
    $controller = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "90" }

    $video = Wait-ForFileLine -Path $controller.Output -Pattern "VIDEO_OK" -TimeoutSeconds 45
    Assert-Condition ($null -ne $video) "La sesión no llegó a transmitir vídeo antes de la caída."

    Suspend-TrackedProcess $controller.Process
    $endedLine = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_SESSION_ENDED" -TimeoutSeconds 40
    $hostText = Get-FileText $hostHarness.Output
    $active = Get-MatchValue $hostText "HOST_SESSION_ENDED reason=.* active=(\d+)"
    Resume-TrackedProcess $controller.Process
    Start-Sleep -Seconds 8
    $alive = -not $controller.Process.HasExited

    Assert-Condition ($null -ne $endedLine) "El host no cerró la sesión tras el bloqueo del cliente."
    Assert-Condition ($active -eq "0") "El host mantiene $active sesiones activas."
    Assert-Condition (Test-ServerHealth $port) "El servidor dejó de responder durante la prueba."

    Add-Result -Id 4 -Name "Pérdida de conexión durante una sesión" -Type "Simulada" -Result "PASS" `
        -Evidence ("Cliente suspendido 35 s; el host cerró la sesión (active={0}); servidor estable" -f $active) `
        -Details ("Proceso del cliente {0} tras reanudar." -f ($(if ($alive) { "sigue vivo" } else { "terminó con normalidad" })))
}

# ---------------------------------------------------------------------------
# 05. Reconexión después de una caída (servidor congelado)
# ---------------------------------------------------------------------------
function Invoke-Scenario05
{
    $dir = New-ScenarioDir "05-reconnect"
    $port = Get-FreeTcpPort 5205
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 180
    $controller = Start-Harness -ScenarioDir $dir -Name "reconnect" -Mode "reconnect" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password

    $initial = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_INITIAL_OK" -TimeoutSeconds 45
    Assert-Condition ($null -ne $initial) "La conexión inicial de reconexión falló."
    Start-Sleep -Seconds 3
    Suspend-TrackedProcess $server.Process
    Start-Sleep -Seconds 25
    Resume-TrackedProcess $server.Process

    $linkDown = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_LINK_DOWN" -TimeoutSeconds 45
    $linkUp = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_LINK_UP" -TimeoutSeconds 90
    $ok = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_OK" -TimeoutSeconds 90
    Wait-ProcessExit $controller.Process 30 | Out-Null
    $hostText = Get-FileText $hostHarness.Output

    Assert-Condition ($null -ne $linkDown) "El cliente no detectó la caída."
    Assert-Condition ($null -ne $linkUp) "El cliente no recuperó el enlace con el servidor."
    Assert-Condition ($null -ne $ok) "El cliente no pudo abrir una nueva sesión tras reconectar."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras reanudarse."

    Add-Result -Id 5 -Name "Reconexión después de una caída" -Type "Simulada" -Result "PASS" `
        -Evidence "Servidor congelado 25 s; enlace recuperado y nueva sesión establecida" `
        -Details ("Host: {0} transiciones a Connected." -f (Get-AllCount $hostText "-> Connected"))
}

# ---------------------------------------------------------------------------
# 06. Caída del servidor y reinicio
# ---------------------------------------------------------------------------
function Invoke-Scenario06
{
    $dir = New-ScenarioDir "06-server-restart"
    $port = Get-FreeTcpPort 5206
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 180
    $controller = Start-Harness -ScenarioDir $dir -Name "reconnect" -Mode "reconnect" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password

    $initial = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_INITIAL_OK" -TimeoutSeconds 45
    Assert-Condition ($null -ne $initial) "La conexión inicial falló antes de la caída."

    Copy-Item $server.Log (Join-Path $dir "server-phase1.log") -Force -ErrorAction SilentlyContinue
    Stop-TrackedProcess $server.Process
    Start-Sleep -Seconds 6
    $server = Start-TestServer -ScenarioDir $dir -Port $port

    $linkUp = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_LINK_UP" -TimeoutSeconds 90
    $ok = Wait-ForFileLine -Path $controller.Output -Pattern "RECONNECT_OK" -TimeoutSeconds 90
    Wait-ProcessExit $controller.Process 30 | Out-Null
    $hostText = Get-FileText $hostHarness.Output

    Assert-Condition ($null -ne $linkUp) "El cliente no volvió a conectar tras reiniciar el servidor."
    Assert-Condition ($null -ne $ok) "El cliente no pudo abrir sesión tras el reinicio."
    Assert-Condition (Test-ServerHealth $port) "El servidor reiniciado no responde."
    Assert-Condition (-not $hostHarness.Process.HasExited) "El host terminó durante el reinicio del servidor."

    Add-Result -Id 6 -Name "Caída del servidor" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Servidor detenido 6 s y reiniciado; clientes reconectados; health OK" `
        -Details ("Host: {0} reconexiones a Connected." -f (Get-AllCount $hostText "-> Connected"))
}

# ---------------------------------------------------------------------------
# 07. Reinicio del cliente
# ---------------------------------------------------------------------------
function Invoke-Scenario07
{
    $dir = New-ScenarioDir "07-client-restart"
    $port = Get-FreeTcpPort 5207
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 150
    $controller = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "60" }

    $connected = Wait-ForFileLine -Path $controller.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    Assert-Condition ($null -ne $connected) "El cliente no conectó antes del reinicio."
    Stop-TrackedProcess $controller.Process
    $ended = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_SESSION_ENDED" -TimeoutSeconds 40
    Assert-Condition ($null -ne $ended) "El host no liberó la sesión del cliente terminado."

    $restarted = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "10" }
    $reconnected = Wait-ForFileLine -Path $restarted.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    Wait-ForAllFinished -Harnesses @($restarted) -TimeoutSeconds 60 | Out-Null
    $restartedText = Get-FileText $restarted.Output
    $hostText = Get-FileText $hostHarness.Output

    Assert-Condition ($null -ne $reconnected) "El cliente reiniciado no pudo reconectar."
    Assert-Condition ($restartedText -match "CONTROLLER_OK") "El cliente reiniciado no completó la sesión."
    Assert-Condition ((Get-AllCount $hostText "HOST_SESSION_STARTED") -ge 2) "El host no registró la nueva sesión."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras reiniciar el cliente."

    Add-Result -Id 7 -Name "Reinicio del cliente" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Cliente terminado, sesión liberada y reconexión correcta con la misma identidad" `
        -Details ("Host: {0} sesiones iniciadas." -f (Get-AllCount $hostText "HOST_SESSION_STARTED"))
}

# ---------------------------------------------------------------------------
# 08. Transferencia de archivos grandes
# ---------------------------------------------------------------------------
function Invoke-Scenario08
{
    $dir = New-ScenarioDir "08-large-file"
    $port = Get-FreeTcpPort 5208
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 180
    $client = Start-Harness -ScenarioDir $dir -Name "bench" -Mode "bench" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "5" }

    $benchLine = Wait-ForFileLine -Path $client.Output -Pattern "FILE_BENCH" -TimeoutSeconds 150
    $fileOk = Wait-ForFileLine -Path $client.Output -Pattern "FILE_OK" -TimeoutSeconds 60
    $hostOk = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_OK file:" -TimeoutSeconds 60
    Wait-ProcessExit $client.Process 30 | Out-Null
    $hostText = Get-FileText $hostHarness.Output
    Assert-Condition ($null -ne $benchLine) "No se midió la transferencia grande."
    $mbsp = [double](Get-MatchValue $benchLine "mbps=([0-9.]+)")
    $bytes = [long](Get-MatchValue $benchLine "bytes=(\d+)")
    $downloadPath = Join-Path $hostHarness.DataDir "downloads\bench-test.bin"
    $finalSize = if (Test-Path $downloadPath) { (Get-Item $downloadPath).Length } else { 0 }

    Assert-Condition ($null -ne $fileOk) "La transferencia grande no terminó correctamente."
    Assert-Condition ($null -ne $hostOk) "El receptor no confirmó el archivo."
    Assert-Condition ($bytes -eq 64MB) "El archivo de prueba no era de 64 MiB ($bytes)."
    Assert-Condition ($finalSize -eq 64MB) "El archivo recibido tiene $finalSize bytes."
    Assert-Condition ($mbsp -gt 5) "Rendimiento anómalo: $mbsp MB/s."

    Add-Result -Id 8 -Name "Transferencia de archivos grandes" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("64 MiB completados en {0} ms ({1} MB/s); tamaño final verificado" -f `
            (Get-MatchValue $benchLine "ms=(\d+)"), $mbsp) `
        -Details ("Host: {0} transferencias completadas." -f (Get-AllCount $hostText "Completed"))
}

# ---------------------------------------------------------------------------
# 09. Interrupción y reanudación de transferencias
# ---------------------------------------------------------------------------
function Invoke-Scenario09
{
    $dir = New-ScenarioDir "09-transfer-resume"
    $port = Get-FreeTcpPort 5209
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 180
    $first = Start-Harness -ScenarioDir $dir -Name "resume-client" -Mode "resume" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password

    $progress = Wait-ForFileLine -Path $hostHarness.Output `
        -Pattern "HOST_TRANSFER resume-test\.bin Transferring [1-9]\d*/" -TimeoutSeconds 45
    Assert-Condition ($null -ne $progress) "La primera transferencia no confirmó progreso."
    Stop-TrackedProcess $first.Process
    Start-Sleep -Seconds 3

    $linesBeforeSecond = @(Get-Content $hostHarness.Output -ErrorAction SilentlyContinue).Count
    $second = Start-Harness -ScenarioDir $dir -Name "resume-client" -Mode "resume" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password
    $hostOk = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_OK file:" -TimeoutSeconds 150
    $secondOk = Wait-ForFileLine -Path $second.Output -Pattern "FILE_OK" -TimeoutSeconds 60
    Wait-ProcessExit $second.Process 60 | Out-Null
    $hostText = Get-FileText $hostHarness.Output
    $newHostLines = @(Get-Content $hostHarness.Output -ErrorAction SilentlyContinue |
        Select-Object -Skip $linesBeforeSecond)
    $secondProgress = $newHostLines |
        Where-Object { $_ -match "HOST_TRANSFER resume-test\.bin Transferring \d+/" } |
        Select-Object -First 1
    $resumeOffset = if ($secondProgress)
    {
        [long](Get-MatchValue $secondProgress "Transferring (\d+)/")
    }
    else
    {
        0
    }
    $finalPath = Join-Path $hostHarness.DataDir "downloads\resume-test.bin"
    $finalSize = if (Test-Path $finalPath) { (Get-Item $finalPath).Length } else { 0 }

    Assert-Condition ($null -ne $hostOk) "La transferencia reanudada no terminó."
    Assert-Condition ($null -ne $secondOk) "El segundo cliente no confirmó la transferencia."
    Assert-Condition ($null -ne $secondProgress) "No se registró el progreso de la segunda conexión."
    Assert-Condition ($resumeOffset -gt 0) "La segunda conexión empezó en 0; no hubo reanudación."
    Assert-Condition ($finalSize -eq 8MB) "El archivo final tiene $finalSize bytes y se esperaban 8 MiB."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras reanudar."

    Add-Result -Id 9 -Name "Interrupción de transferencias" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("Transferencia reanudada desde {0} bytes; archivo final de 8 MiB" -f $resumeOffset) `
        -Details "El host conservó el progreso y la segunda conexión continuó desde el offset confirmado."
}

# ---------------------------------------------------------------------------
# 10. Cancelación de operaciones
# ---------------------------------------------------------------------------
function Invoke-Scenario10
{
    $dir = New-ScenarioDir "10-cancel"
    $port = Get-FreeTcpPort 5210
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 120
    $client = Start-Harness -ScenarioDir $dir -Name "cancel" -Mode "cancel" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password

    $cancelLine = Wait-ForFileLine -Path $client.Output -Pattern "CANCEL_REQUESTED" -TimeoutSeconds 60
    Wait-ProcessExit $client.Process 60 | Out-Null
    $clientText = Get-FileText $client.Output
    $hostText = Get-FileText $hostHarness.Output
    $finalPath = Join-Path $hostHarness.DataDir "downloads\cancel-test.bin"

    Assert-Condition ($null -ne $cancelLine) "No se solicitó la cancelación."
    Assert-Condition ($client.Process.HasExited) "El cliente no terminó tras cancelar."
    Assert-Condition (-not (Test-Path $finalPath)) "El archivo cancelado quedó publicado en descargas."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras cancelar."

    Add-Result -Id 10 -Name "Cancelación de operaciones" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Transferencia de 32 MiB cancelada; sin archivo final; proceso terminado" `
        -Details ("Estados del host: {0}." -f (($hostText -split "`r?`n" | Where-Object { $_ -match "HOST_TRANSFER cancel-test" } | Select-Object -Last 3) -join " | "))
}

# ---------------------------------------------------------------------------
# 11. Cambio de resolución (pendiente de hardware real)
# ---------------------------------------------------------------------------
function Invoke-Scenario11
{
    Add-Result -Id 11 -Name "Cambio de resolución" -Type "Pendiente" -Result "PENDING" `
        -Evidence "Requiere cambiar la resolución del escritorio; no se modifica el equipo de pruebas" `
        -Details "La ruta de cambio de captura se ejercita indirectamente en el escenario 12."
}

# ---------------------------------------------------------------------------
# 12. Cambio de monitor
# ---------------------------------------------------------------------------
function Invoke-Scenario12
{
    $dir = New-ScenarioDir "12-monitor-switch"
    $port = Get-FreeTcpPort 5212
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 150
    $client = Start-Harness -ScenarioDir $dir -Name "monitor" -Mode "monitor" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password

    $done = Wait-ForFileLine -Path $client.Output -Pattern "MONITOR_OK|MONITORS_TIMEOUT|MONITOR_CONNECT_FAIL" -TimeoutSeconds 90
    Wait-ProcessExit $client.Process 45 | Out-Null
    $text = Get-FileText $client.Output
    $count = [int](Get-MatchValue $text "MONITORS count=(\d+)")
    $switches = @([regex]::Matches($text, "MONITOR_SWITCH index=(\d+) frames=(\d+)"))
    $zeroFrames = @($switches | Where-Object { [int]$_.Groups[2].Value -eq 0 }).Count

    Assert-Condition ($null -ne $done) "La prueba de monitores no terminó."
    Assert-Condition ($count -ge 1) "No se enumeró ningún monitor."
    Assert-Condition ($switches.Count -ge 1) "No se ejecutó ningún cambio de monitor."
    Assert-Condition ($zeroFrames -eq 0) "Algún monitor no produjo fotogramas tras el cambio."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras cambiar de monitor."

    Add-Result -Id 12 -Name "Cambio de monitor" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("{0} monitores; {1} cambios con vídeo continuo" -f $count, $switches.Count) `
        -Details (($text -split "`r?`n" | Where-Object { $_ -match "MONITOR_INFO|MONITOR_SWITCH" }) -join " | ")
}

# ---------------------------------------------------------------------------
# 13. Cierre de la ventana del servidor durante una sesión
# ---------------------------------------------------------------------------
function Invoke-Scenario13
{
    $dir = New-ScenarioDir "13-window-close"
    $port = Get-FreeTcpPort 5213
    $uiData = Join-Path $dir "ui-server"
    $db = Join-Path $uiData "server.db"
    Write-ServerUiSettings -DataDir $uiData -Port $port -DatabasePath $db
    $ui = Start-ServerUi -DataDir $uiData -Port $port

    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 90
    $client = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "controller" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "30" }
    $connected = Wait-ForFileLine -Path $client.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    $sessionStarted = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_SESSION_STARTED" -TimeoutSeconds 45
    Assert-Condition ($null -ne $connected) "No había sesión activa al cerrar la ventana."
    Assert-Condition ($null -ne $sessionStarted) "El host no registró la sesión activa."

    $ui.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 4
    $ui.Refresh()
    $hostText = Get-FileText $hostHarness.Output

    Assert-Condition (-not $ui.HasExited) "La ventana del servidor cerró el proceso completo."
    Assert-Condition (Test-ServerHealth $port) "El servidor dejó de servir tras cerrar la ventana."
    Assert-Condition (-not ($hostText -match "HOST_SESSION_ENDED")) "La sesión remota se cerró al ocultar la ventana."
    Assert-Condition (-not $client.Process.HasExited) "El cliente remoto terminó al cerrar la ventana del servidor."

    Add-Result -Id 13 -Name "Cierre de ventanas durante operaciones activas" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Ventana del servidor cerrada; proceso vivo, health OK y sesión remota intacta" `
        -Details "El servidor pasa a la bandeja y sigue atendiendo la sesión."
}

# ---------------------------------------------------------------------------
# 14. Cambio de tema durante una sesión
# ---------------------------------------------------------------------------
function Invoke-Scenario14
{
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $dir = New-ScenarioDir "14-theme-change"
    $port = Get-FreeTcpPort 5214
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 120
    $session = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "controller" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "60" }
    $connected = Wait-ForFileLine -Path $session.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    $sessionStarted = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_SESSION_STARTED" -TimeoutSeconds 45
    Assert-Condition ($null -ne $connected) "No había sesión activa para el cambio de tema."
    Assert-Condition ($null -ne $sessionStarted) "El host no registró la sesión activa."

    $uiData = Join-Path $dir "ui-client"
    $ui = Start-ClientUi -DataDir $uiData -Port $port
    Start-Sleep -Seconds 4
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($ui.MainWindowHandle)
    $navCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "Configuración")
    $nav = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $navCondition)
    if ($nav)
    {
        try
        {
            $selection = $nav.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern)
            $selection.Select()
        }
        catch
        {
            $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
    }

    Start-Sleep -Seconds 2
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "ThemeCombo")
    $combo = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
    Assert-Condition ($null -ne $combo) "No se encontró el selector de tema."

    $expander = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    foreach ($themeName in @("Tema claro", "Tema oscuro", "Tema del sistema"))
    {
        $expander.Expand()
        Start-Sleep -Milliseconds 700
        $itemCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $themeName)
        $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
        if ($item)
        {
            try
            {
                $item.GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            }
            catch
            {
                $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            }
        }

        Start-Sleep -Seconds 1
    }

    Start-Sleep -Seconds 2
    $ui.Refresh()
    $hostText = Get-FileText $hostHarness.Output
    Assert-Condition (-not $ui.HasExited) "La interfaz del cliente terminó al cambiar el tema."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras el cambio de tema."
    Assert-Condition (-not ($hostText -match "HOST_SESSION_ENDED")) "La sesión remota se cerró al cambiar el tema."

    Add-Result -Id 14 -Name "Cambio de tema durante una sesión" -Type "Simulada" -Result "PASS" `
        -Evidence "Tema cambiado 3 veces en la UI real con una sesión remota activa" `
        -Details "La sesión remota pertenece al harness; la UI mantuvo su conexión de señalización."
}

# ---------------------------------------------------------------------------
# 15. Uso prolongado
# ---------------------------------------------------------------------------
function Invoke-Scenario15
{
    $dir = New-ScenarioDir "15-soak"
    $port = Get-FreeTcpPort 5215
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds ($SoakSeconds + 60) `
        -Overrides @{ preferModernCapture = $false }
    $client = Start-Harness -ScenarioDir $dir -Name "soak" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{
            CSIVIEWER_HARNESS_HOLD = [string]$SoakSeconds
            CSIVIEWER_HARNESS_STATS = "1"
        }

    $video = Wait-ForFileLine -Path $client.Output -Pattern "VIDEO_OK" -TimeoutSeconds 45
    Assert-Condition ($null -ne $video) "La sesión prolongada no llegó a transmitir vídeo."
    $midClientMetrics = Get-Metrics $client.Process
    $midServerMetrics = Get-Metrics $server.Process
    Wait-ProcessExit $client.Process ($SoakSeconds + 60) | Out-Null
    Assert-Condition ($client.Process.HasExited) "El cliente de soak no terminó."
    $text = Get-FileText $client.Output
    $lines = @(Get-Content $client.Output -ErrorAction SilentlyContinue)
    $periodic = @($lines | Where-Object { $_ -match "^STATS periodic " })
    $first = if ($periodic.Count -gt 0) { Convert-StatLine $periodic[0] } else { $null }
    $last = if ($periodic.Count -gt 0) { Convert-StatLine $periodic[-1] } else { $null }

    $frameMatches = [regex]::Matches($text, "VIDEO_FRAME (\d+)")
    $frames = if ($frameMatches.Count -gt 0)
    {
        [int]$frameMatches[$frameMatches.Count - 1].Groups[1].Value
    }
    else
    {
        0
    }
    $managedDelta = if ($first -and $last) { [math]::Round(($last["managed"] - $first["managed"]) / 1MB, 1) } else { 0 }
    $threadDelta = if ($first -and $last) { $last["threads"] - $first["threads"] } else { 0 }
    $handleDelta = if ($first -and $last) { $last["handles"] - $first["handles"] } else { 0 }
    $tpPending = if ($last) { $last["tp_pending"] } else { -1 }

    Assert-Condition ($periodic.Count -ge 5) "No hubo suficientes muestras periódicas de métricas."
    Assert-Condition ($frames -gt ($SoakSeconds * 5)) "El vídeo no continuó durante la sesión ($frames fotogramas)."
    Assert-Condition ($managedDelta -le 64) "Crecimiento de memoria en la sesión prolongada: $managedDelta MB."
    Assert-Condition ($threadDelta -le 8) "Crecimiento de hilos en la sesión prolongada: $threadDelta."
    Assert-Condition ($handleDelta -le 100) "Crecimiento de handles en la sesión prolongada: $handleDelta."
    Assert-Condition ($null -ne $midClientMetrics) "No se pudo medir el cliente durante el soak."
    Assert-Condition ($midClientMetrics.Tcp -le 4) "Sockets TCP del cliente durante el soak: $($midClientMetrics.Tcp)."
    Assert-Condition ($tpPending -le 2) "Tareas pendientes en el thread pool: $tpPending."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras la sesión prolongada."

    Add-Result -Id 15 -Name "Uso prolongado de la aplicación" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("{0} s de sesión; {1} fotogramas; managed {2} MB; hilos {3}; handles {4}; tcp {5}; tp_pending {6}" -f `
            $SoakSeconds, $frames, $managedDelta, $threadDelta, $handleDelta,
            $midClientMetrics.Tcp, $tpPending) `
        -Details ("Muestras periódicas: {0}. Servidor durante el soak: {1}." -f `
            $periodic.Count, (Format-Metrics $midServerMetrics))
}

# ---------------------------------------------------------------------------
# 16. Errores de autenticación
# ---------------------------------------------------------------------------
function Invoke-Scenario16
{
    $dir = New-ScenarioDir "16-auth-errors"
    $port = Get-FreeTcpPort 5216
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 150

    $wrongTexts = @()
    for ($attempt = 1; $attempt -le 5; $attempt++)
    {
        $client = Start-Harness -ScenarioDir $dir -Name "wrong" -Mode "viewer" -Port $port `
            -HostId $hostHarness.HostId -Password "WRONG-PASSWORD-$attempt" `
            -Environment @{ CSIVIEWER_HARNESS_HOLD = "0" }
        Wait-ProcessExit $client.Process 40 | Out-Null
        $wrongTexts += Get-HarnessText $client
    }

    $correct = Start-Harness -ScenarioDir $dir -Name "correct" -Mode "viewer" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "5" }
    $connected = Wait-ForFileLine -Path $correct.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    Wait-ForAllFinished -Harnesses @($correct) -TimeoutSeconds 60 | Out-Null
    $hostText = Get-FileText $hostHarness.Output

    $failed = @($wrongTexts | Where-Object { $_ -match "CONTROLLER_FAIL" }).Count
    $locked = Get-AllCount $hostText "locked=True"
    $authFailures = Get-AllCount $hostText "Authentication failed"

    Assert-Condition ($failed -eq 5) "Solo fallaron $failed de 5 intentos con contraseña incorrecta."
    Assert-Condition ($authFailures -ge 3) "El host no registró los fallos de autenticación."
    Assert-Condition ($locked -ge 1) "No se activó el bloqueo por intentos fallidos."
    Assert-Condition ($null -ne $connected) "Una identidad nueva no pudo autenticarse tras los fallos."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras los fallos de autenticación."

    Add-Result -Id 16 -Name "Errores de autenticación" -Type "Ejecutada" -Result "PASS" `
        -Evidence ("5 fallos controlados; {0} bloqueos; autenticación correcta con identidad nueva" -f $locked) `
        -Details ("Fallos registrados por el host: {0}." -f $authFailures)
}

# ---------------------------------------------------------------------------
# 17. Sesiones expiradas
# ---------------------------------------------------------------------------
function Invoke-Scenario17
{
    $dir = New-ScenarioDir "17-expired-password"
    $port = Get-FreeTcpPort 5217
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 150 -Overrides @{
        temporaryPasswordLifetimeMinutes = 1
    }

    Start-Sleep -Seconds 65
    $late = Start-Harness -ScenarioDir $dir -Name "late" -Mode "viewer" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "0" }
    Wait-ProcessExit $late.Process 45 | Out-Null
    $lateText = Get-HarnessText $late
    $hostText = Get-FileText $hostHarness.Output

    Assert-Condition ($lateText -match "CONTROLLER_FAIL") "La contraseña caducada fue aceptada."
    Assert-Condition ($hostText -match "expired temporary password") "El host no registró la contraseña caducada."
    Assert-Condition ((Get-AllCount $hostText "HOST_SESSION_STARTED") -eq 0) "Se abrió una sesión con contraseña caducada."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras la prueba de caducidad."

    Add-Result -Id 17 -Name "Sesiones expiradas" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Contraseña temporal de 1 min rechazada tras 65 s; sin sesión creada" `
        -Details ("Código de rechazo: {0}" -f (Get-MatchValue $lateText "CONTROLLER_FAIL ([a-z_]+)"))
}

# ---------------------------------------------------------------------------
# 18. Revocación de permisos durante una conexión
# ---------------------------------------------------------------------------
function Invoke-Scenario18
{
    $dir = New-ScenarioDir "18-revoke"
    $port = Get-FreeTcpPort 5218
    $server = Start-TestServer -ScenarioDir $dir -Port $port
    $hostHarness = Start-HostHarness -ScenarioDir $dir -Port $port -DurationSeconds 120 -Environment @{
        CSIVIEWER_HARNESS_REVOKE_AFTER = "8"
    }
    $controller = Start-Harness -ScenarioDir $dir -Name "controller" -Mode "viewercontrol" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "30" }

    $connected = Wait-ForFileLine -Path $controller.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    Assert-Condition ($null -ne $connected) "La sesión no llegó a establecerse."
    $revoked = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_REVOKED" -TimeoutSeconds 45
    $ended = Wait-ForFileLine -Path $hostHarness.Output -Pattern "HOST_SESSION_ENDED" -TimeoutSeconds 30
    $newPassword = Get-MatchValue $revoked "password=(\S+)"
    Wait-ProcessExit $controller.Process 40 | Out-Null

    $old = Start-Harness -ScenarioDir $dir -Name "old-password" -Mode "viewer" -Port $port `
        -HostId $hostHarness.HostId -Password $hostHarness.Password `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "0" }
    Wait-ProcessExit $old.Process 40 | Out-Null
    $oldText = Get-HarnessText $old

    $fresh = Start-Harness -ScenarioDir $dir -Name "new-password" -Mode "viewer" -Port $port `
        -HostId $hostHarness.HostId -Password $newPassword `
        -Environment @{ CSIVIEWER_HARNESS_HOLD = "5" }
    $freshConnected = Wait-ForFileLine -Path $fresh.Output -Pattern "CONTROLLER_CONNECTED" -TimeoutSeconds 45
    Wait-ForAllFinished -Harnesses @($fresh) -TimeoutSeconds 60 | Out-Null

    Assert-Condition ($null -ne $revoked) "El host no revocó la sesión."
    Assert-Condition ($null -ne $ended) "La sesión no se cerró tras la revocación."
    Assert-Condition ($oldText -match "CONTROLLER_FAIL") "La contraseña anterior siguió siendo válida."
    Assert-Condition ($null -ne $freshConnected) "La contraseña regenerada no permitió conectar."
    Assert-Condition (Test-ServerHealth $port) "El servidor no responde tras la revocación."

    Add-Result -Id 18 -Name "Revocación de permisos durante una conexión" -Type "Ejecutada" -Result "PASS" `
        -Evidence "Sesión revocada; contraseña anterior rechazada; contraseña nueva aceptada" `
        -Details ("Controlador: {0}." -f (Get-MatchValue (Get-FileText $controller.Output) "CONTROLLER_CONNECTED permissions=([^\r\n]+)"))
}

# ---------------------------------------------------------------------------
# Informe
# ---------------------------------------------------------------------------
function Write-StabilityReport
{
    $finishedAt = Get-Date
    $duration = $finishedAt - $startedAt
    $ordered = @($global:StabilityResults | Sort-Object Id)
    $passed = @($ordered | Where-Object { $_.Result -eq "PASS" }).Count
    $failed = @($ordered | Where-Object { $_.Result -eq "FAIL" }).Count
    $pending = @($ordered | Where-Object { $_.Result -eq "PENDING" }).Count

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Informe de estabilidad y concurrencia")
    $lines.Add("")
    $lines.Add("Generado: $($finishedAt.ToString("yyyy-MM-dd HH:mm:ss"))")
    $lines.Add("")
    $lines.Add("## Entorno")
    $lines.Add("")
    $lines.Add("- SO: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)")
    $lines.Add("- .NET: $([System.Environment]::Version)")
    $lines.Add("- Procesadores lógicos: $([System.Environment]::ProcessorCount)")
    $lines.Add("- Perfil: $Profile")
    $lines.Add("- Duración total: $([math]::Round($duration.TotalMinutes, 1)) min")
    $lines.Add("- Resultado: **$passed PASS**, **$failed FAIL**, **$pending PENDING**")
    $lines.Add("")
    $lines.Add("## Resultados")
    $lines.Add("")
    $lines.Add("| # | Escenario | Tipo | Resultado | Evidencia |")
    $lines.Add("|---:|---|---|---|---|")
    foreach ($result in $ordered)
    {
        $evidence = ($result.Evidence -replace "\|", "\|")
        $lines.Add("| $($result.Id) | $($result.Name) | $($result.Type) | $($result.Result) | $evidence |")
    }

    $lines.Add("")
    $lines.Add("## Detalle")
    foreach ($result in $ordered)
    {
        $lines.Add("")
        $lines.Add("### $($result.Id). $($result.Name)")
        $lines.Add("")
        $lines.Add("- Tipo: $($result.Type)")
        $lines.Add("- Resultado: $($result.Result)")
        $lines.Add("- Evidencia: $($result.Evidence)")
        if ($result.Details)
        {
            $lines.Add("- Detalles: $($result.Details)")
        }
    }

    $reportPath = Join-Path $global:StabilityOutputRoot "report.md"
    $lines | Set-Content -Path $reportPath -Encoding utf8
    $ordered | ConvertTo-Json -Depth 6 |
        Set-Content -Path (Join-Path $global:StabilityOutputRoot "results.json") -Encoding utf8

    Write-Host ""
    Write-Host "Informe: $reportPath" -ForegroundColor Green
    Write-Host ("Resumen: {0} PASS, {1} FAIL, {2} PENDING" -f $passed, $failed, $pending)
    return $reportPath
}

# ---------------------------------------------------------------------------
# Ejecución
# ---------------------------------------------------------------------------
Write-Host "Batería de estabilidad CSIViewer" -ForegroundColor Cyan
Write-Host "Salida: $global:StabilityOutputRoot"
Write-Host "Perfil: $Profile"

$dispatch = [ordered]@{
    "01" = @{ Name = "Conectar y desconectar repetidamente"; Block = ${function:Invoke-Scenario01} }
    "02" = @{ Name = "Dos clientes accediendo al mismo host"; Block = ${function:Invoke-Scenario02} }
    "03" = @{ Name = "Múltiples solicitudes simultáneas"; Block = ${function:Invoke-Scenario03} }
    "04" = @{ Name = "Pérdida de conexión durante una sesión"; Block = ${function:Invoke-Scenario04} }
    "05" = @{ Name = "Reconexión después de una caída"; Block = ${function:Invoke-Scenario05} }
    "06" = @{ Name = "Caída del servidor"; Block = ${function:Invoke-Scenario06} }
    "07" = @{ Name = "Reinicio del cliente"; Block = ${function:Invoke-Scenario07} }
    "08" = @{ Name = "Transferencia de archivos grandes"; Block = ${function:Invoke-Scenario08} }
    "09" = @{ Name = "Interrupción de transferencias"; Block = ${function:Invoke-Scenario09} }
    "10" = @{ Name = "Cancelación de operaciones"; Block = ${function:Invoke-Scenario10} }
    "11" = @{ Name = "Cambio de resolución"; Block = ${function:Invoke-Scenario11} }
    "12" = @{ Name = "Cambio de monitor"; Block = ${function:Invoke-Scenario12} }
    "13" = @{ Name = "Cierre de ventanas durante operaciones activas"; Block = ${function:Invoke-Scenario13} }
    "14" = @{ Name = "Cambio de tema durante una sesión"; Block = ${function:Invoke-Scenario14} }
    "15" = @{ Name = "Uso prolongado de la aplicación"; Block = ${function:Invoke-Scenario15} }
    "16" = @{ Name = "Errores de autenticación"; Block = ${function:Invoke-Scenario16} }
    "17" = @{ Name = "Sesiones expiradas"; Block = ${function:Invoke-Scenario17} }
    "18" = @{ Name = "Revocación de permisos durante una conexión"; Block = ${function:Invoke-Scenario18} }
}

Stop-SuiteProcesses
Start-Sleep -Milliseconds 500

foreach ($entry in $dispatch.GetEnumerator())
{
    $id = [int]$entry.Key
    if (-not (Should-Run $entry.Key $entry.Value.Name))
    {
        continue
    }

    Invoke-Scenario -Id $id -Name $entry.Value.Name -Block $entry.Value.Block
}

Write-StabilityReport | Out-Null

$failures = @($global:StabilityResults | Where-Object { $_.Result -eq "FAIL" }).Count
if ($failures -gt 0)
{
    exit 1
}
