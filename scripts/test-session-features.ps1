param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$data=Join-Path $env:LOCALAPPDATA ('Temp/opencode/feature-server-'+[Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $data
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$url="http://127.0.0.1:$port"
$server=Join-Path $root "src/CSIViewer.Server/bin/$Configuration/net10.0-windows10.0.19041.0/win-x64/CSIViewerServidor.exe"
$probe=Join-Path $root "tools/CSIViewer.FeatureProbe/bin/$Configuration/net10.0-windows10.0.19041.0/CSIViewer.FeatureProbe.exe"
$output=Join-Path $root "artifacts/diagnostics/session-features-$($Configuration.ToLowerInvariant()).json"
$previous=$env:CSIVIEWER_SERVER_DATA_DIR
$process=$null
try {
 $env:CSIVIEWER_SERVER_DATA_DIR=$data
 $process=Start-Process $server -ArgumentList @('--modo=server',"--port=$port",'--local-only','--enrollment-code=FEATURE-QA','--no-upnp','--no-stun') -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $data 'server.stdout.log') -RedirectStandardError (Join-Path $data 'server.stderr.log')
 $ready=$false
 for($i=0;$i-lt 50 -and -not $process.HasExited;$i++){
  try {$ready=(Invoke-WebRequest "$url/api/health" -TimeoutSec 1 -UseBasicParsing).StatusCode -eq 200}catch{}
  if($ready){break};Start-Sleep -Milliseconds 200
 }
 if(-not $ready){throw "Isolated server unavailable; see $data"}
 & $probe $url $output
 if($LASTEXITCODE-ne 0){throw "Feature probe failed; see $output"}
 Get-Content -LiteralPath $output -Raw
} finally {
 $env:CSIVIEWER_SERVER_DATA_DIR=$previous
 if($process -and -not $process.HasExited){Stop-Process -Id $process.Id}
}
