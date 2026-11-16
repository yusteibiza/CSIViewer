$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root ('artifacts/audit/comparison-smoke-'+[Guid]::NewGuid().ToString('N'))
$data=Join-Path $fixture 'runs/Host-synthetic'
$installed=Join-Path $fixture 'installed'
$installedData=Join-Path $fixture 'installed-data'
New-Item -ItemType Directory -Path $data,$installed,$installedData,(Join-Path $fixture 'host') -Force|Out-Null
Copy-Item ([Uri].Assembly.Location) (Join-Path $installed 'CSIViewer.Host.dll')
Copy-Item ([Uri].Assembly.Location) (Join-Path $fixture 'host/CSIViewer.Host.dll')
Copy-Item (Join-Path $PSScriptRoot 'compare-host-diagnostic.ps1') (Join-Path $fixture 'Compare-Host.ps1')
'synthetic'|Set-Content (Join-Path $data '.csiviewer-diagnostic-data')
'{"BuildId":"synthetic"}'|Set-Content (Join-Path $fixture 'build-manifest.json')
$sentinel='SYNTHETIC_SECRET_MUST_NEVER_APPEAR'
@{Fps=90;MaxWidth=1920;ImageQuality=62;PreferModernCapture=$true;PreferH264=$true;ServerUrl=$sentinel;EnrollmentCode=$sentinel;ProtectedEnrollmentCode=$sentinel}|
    ConvertTo-Json|Set-Content (Join-Path $installedData 'settings.json')
@{Fps=30;MaxWidth=1280;ImageQuality=62;PreferModernCapture=$true;PreferH264=$true;ServerUrl=$sentinel}|
    ConvertTo-Json|Set-Content (Join-Path $data 'settings.json')
$before=(Get-FileHash (Join-Path $installedData 'settings.json')).Hash
& (Join-Path $fixture 'Compare-Host.ps1') -RunName synthetic -InstalledDirectory $installed -InstalledDataDirectory $installedData -SkipProcessInspection
$file=Get-ChildItem $data -Filter host-comparison-*.json
$text=Get-Content $file.FullName -Raw
if($text.Contains($sentinel)){throw 'Secret leaked'}
$json=$text|ConvertFrom-Json
if($json.InstalledVideoSettings.Values.Fps -ne 90 -or $json.PortableVideoSettings.Values.Fps -ne 30){throw 'Incorrect preference comparison'}
$binary=@($json.Binaries|Where-Object { $_.Origin -eq 'Installed' -and $_.File -eq 'CSIViewer.Host.dll' })
if($binary.Count -ne 1 -or $binary[0].SHA256 -ne (Get-FileHash ([Uri].Assembly.Location)).Hash){throw 'Incorrect binary identity'}
if((Get-FileHash (Join-Path $installedData 'settings.json')).Hash -ne $before){throw 'Settings mutated'}
$bad=Join-Path $fixture 'runs/Host-invalid'
New-Item -ItemType Directory -Path $bad|Out-Null
'synthetic'|Set-Content (Join-Path $bad '.csiviewer-diagnostic-data')
@{Fps=$sentinel;PreferH264=$sentinel;Password=$sentinel}|ConvertTo-Json|Set-Content (Join-Path $bad 'settings.json')
& (Join-Path $fixture 'Compare-Host.ps1') -RunName invalid -InstalledDirectory $installed -InstalledDataDirectory $installedData -SkipProcessInspection
$file=Get-ChildItem $bad -Filter host-comparison-*.json
$text=Get-Content $file.FullName -Raw
if($text.Contains($sentinel)){throw 'Invalid typed setting leaked'}
$json=$text|ConvertFrom-Json
if($json.PortableVideoSettings.Values.Fps -ne 'InvalidTypeOrRange'){throw 'Invalid numeric type not rejected'}
Write-Host "PASS: whitelist, secret exclusion, invalid types, missing binaries and read-only installed settings. Synthetic only: $fixture"
