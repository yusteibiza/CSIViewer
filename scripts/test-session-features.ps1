param([string]$Configuration='Release',[string]$UiExe,[string]$HostUiExe,[string]$ViewerConsentExe,[switch]$VideoSession,[switch]$ControlLease,[switch]$ConnectionWaitUi,[string]$UiHarness=(Join-Path $env:LOCALAPPDATA 'Temp/opencode/test-accent-help.ps1'))
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
$peerProcess=$null
$peerOutput=Join-Path $data 'ui-peer.json'
try {
 $env:CSIVIEWER_SERVER_DATA_DIR=$data
 $process=Start-Process $server -ArgumentList @('--modo=server',"--port=$port",'--local-only','--enrollment-code=FEATURE-QA','--no-upnp','--no-stun') -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $data 'server.stdout.log') -RedirectStandardError (Join-Path $data 'server.stderr.log')
 $ready=$false
 for($i=0;$i-lt 50 -and -not $process.HasExited;$i++){
  try {$ready=(Invoke-WebRequest "$url/api/health" -TimeoutSec 1 -UseBasicParsing).StatusCode -eq 200}catch{}
  if($ready){break};Start-Sleep -Milliseconds 200
 }
 if(-not $ready){throw "Isolated server unavailable; see $data"}
  if($ControlLease){
    $output=Join-Path $root "artifacts/diagnostics/control-lease-$($Configuration.ToLowerInvariant()).json"
    & $probe $url $output --control-lease
    if($LASTEXITCODE-ne 0){throw "Control lease diagnostic failed; see $output"}
    Get-Content -LiteralPath $output -Raw
  }elseif($VideoSession){
   $output=Join-Path $root "artifacts/diagnostics/video-session-$($Configuration.ToLowerInvariant()).json"
   & $probe $url $output --video-session
   if($LASTEXITCODE-ne 0){throw "Video diagnostic failed; see $output"}
   Get-Content -LiteralPath $output -Raw
 }elseif($ViewerConsentExe){
   $uiOutput=Join-Path $root "artifacts/diagnostics/viewer-incoming-consent-$($Configuration.ToLowerInvariant()).json"
   @{Url=$url}|ConvertTo-Json|Set-Content ($peerOutput+'.url.json') -Encoding utf8
   & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $UiHarness -Exe $ViewerConsentExe -Kind viewer -ViewerConsentUi -ControllerProbe $probe -ChatPeerFile ($peerOutput+'.url.json') -OutputFile $uiOutput
   if($LASTEXITCODE-ne 0 -or -not (Test-Path -LiteralPath $uiOutput)){throw 'Viewer consent diagnostic did not complete'}
   $checks=@(Get-Content -LiteralPath $uiOutput -Raw|ConvertFrom-Json)
   if(-not $checks.Count -or @($checks|Where-Object {-not $_.Passed}).Count){throw "Viewer consent diagnostic failed; see $uiOutput"}
 }elseif($HostUiExe){
   $uiOutput=Join-Path $root "artifacts/diagnostics/temporary-host-ui-$($Configuration.ToLowerInvariant()).json"
   @{Url=$url}|ConvertTo-Json|Set-Content ($peerOutput+'.url.json') -Encoding utf8
   & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $UiHarness -Exe $HostUiExe -Kind host -HostSessionUi -ControllerProbe $probe -ChatPeerFile ($peerOutput+'.url.json') -OutputFile $uiOutput
   if($LASTEXITCODE-ne 0 -or -not (Test-Path -LiteralPath $uiOutput)){throw 'Host UI diagnostic did not complete'}
   $checks=@(Get-Content -LiteralPath $uiOutput -Raw|ConvertFrom-Json)
   if(-not $checks.Count -or @($checks|Where-Object {-not $_.Passed}).Count){throw "Host UI diagnostic failed; see $uiOutput"}
 }elseif($UiExe){
   $peerMode=if($ConnectionWaitUi){'--ui-host-delayed'}else{'--ui-host'}
   $peerProcess=Start-Process $probe -ArgumentList @($url,"`"$peerOutput`"",$peerMode) -PassThru -RedirectStandardOutput (Join-Path $data 'peer.stdout.log') -RedirectStandardError (Join-Path $data 'peer.stderr.log')
   for($i=0;$i-lt 200 -and -not (Test-Path ($peerOutput+'.peer.json')) -and -not $peerProcess.HasExited;$i++){Start-Sleep -Milliseconds 100}
   if(-not (Test-Path ($peerOutput+'.peer.json'))){throw 'UI chat peer unavailable'}
   if(-not (Test-Path -LiteralPath $UiHarness)){throw "UI diagnostic harness unavailable: $UiHarness"}
   $uiName=if($ConnectionWaitUi){'connection-wait-viewer'}else{'chat-emoji-viewer'}
   $uiOutput=Join-Path $root "artifacts/diagnostics/$uiName-$($Configuration.ToLowerInvariant()).json"
   if($ConnectionWaitUi){
     & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $UiHarness -Exe $UiExe -Kind viewer -ConnectionWaitUi -ChatPeerFile ($peerOutput+'.peer.json') -OutputFile $uiOutput
   }else{
     & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $UiHarness -Exe $UiExe -Kind viewer -EmojiUi -ChatPeerFile ($peerOutput+'.peer.json') -OutputFile $uiOutput
   }
   if($LASTEXITCODE-ne 0 -or -not (Test-Path -LiteralPath $uiOutput)){throw 'UI diagnostic did not complete'}
   $checks=@(Get-Content -LiteralPath $uiOutput -Raw|ConvertFrom-Json)
   if(-not $checks.Count -or @($checks|Where-Object {-not $_.Passed}).Count){throw "UI diagnostic failed; see $uiOutput"}
 }else{
   & $probe $url $output
   if($LASTEXITCODE-ne 0){throw "Feature probe failed; see $output"}
   Get-Content -LiteralPath $output -Raw
 }
} finally {
 if($peerProcess -and -not $peerProcess.HasExited){New-Item -ItemType File -Path ($peerOutput+'.stop') -Force|Out-Null;if(-not $peerProcess.WaitForExit(10000)){Stop-Process -Id $peerProcess.Id}}
 $env:CSIVIEWER_SERVER_DATA_DIR=$previous
 if($process -and -not $process.HasExited){Stop-Process -Id $process.Id}
}
