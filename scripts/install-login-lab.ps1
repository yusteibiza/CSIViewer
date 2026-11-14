param(
    [ValidateSet('Install','Configure','Start','Stop','Status','RemoveTask')][string]$Mode='Status',
    [switch]$ConfirmVm
)
$ErrorActionPreference='Stop'
$machine=Get-CimInstance Win32_ComputerSystem
if(-not $ConfirmVm -or "$($machine.Manufacturer) $($machine.Model)" -notmatch 'VMware'){
    throw 'This experimental package may be installed ONLY inside the VMware test VM with -ConfirmVm.'
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run PowerShell as administrator INSIDE the VM.'}
$install=Join-Path $env:ProgramFiles 'CSIViewer Host Login Lab'
$data=Join-Path $env:ProgramData 'CSIViewer.Host.Login.Lab'
$exe=Join-Path $install 'CSIViewer.Host.Login.exe'
$taskName='CSIViewerHostLoginLab'

function Assert-OwnedTask {
    $task=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if($task){
        $actions=@($task.Actions)
        if($actions.Count-ne 1 -or $actions[0].Execute-ne $exe -or $actions[0].Arguments-ne '--broker' -or $task.Principal.UserId-notin @('SYSTEM','S-1-5-18')){
            throw 'Unexpected task with the lab task name. Refusing to change it.'
        }
    }
    return $task
}
function Invoke-Lab([string]$Argument){
    & $exe $Argument
    if($LASTEXITCODE-ne 0){throw 'Login lab command failed. Do not continue; report the displayed error and failure.json if present.'}
}
function New-SecureDirectory([string]$Path){
    if(Test-Path -LiteralPath $Path){
        if((Get-Item -LiteralPath $Path -Force).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Redirected paths are not permitted.'}
        return
    }
    $parent=Split-Path -Parent $Path
    if(-not (Test-Path -LiteralPath $parent)){New-SecureDirectory $parent}
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetSecurityDescriptorSddlForm('O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
    $info=New-Object IO.DirectoryInfo($Path)
    if($PSVersionTable.PSEdition-eq 'Desktop'){$info.Create($acl)}else{[IO.FileSystemAclExtensions]::Create($info,$acl)}
}
function Copy-SecureFile([string]$Source,[string]$Destination,[string]$ExpectedHash){
    if(Test-Path -LiteralPath $Destination){throw 'Refusing to overwrite an existing payload file.'}
    New-SecureDirectory (Split-Path -Parent $Destination)
    $acl=New-Object Security.AccessControl.FileSecurity
    $acl.SetSecurityDescriptorSddlForm('O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
    $inputStream=[IO.File]::OpenRead($Source)
    try{
        # Protect ownership/DACL at creation, not after copying from a user-writable ZIP.
        if($PSVersionTable.PSEdition-eq 'Desktop'){
            $outputStream=[IO.File]::Create($Destination,4096,[IO.FileOptions]::None,$acl)
        }else{
            $info=New-Object IO.FileInfo($Destination)
            $outputStream=[IO.FileSystemAclExtensions]::Create($info,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::FullControl,[IO.FileShare]::None,4096,[IO.FileOptions]::None,$acl)
        }
        try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose()}
    }finally{$inputStream.Dispose()}
    if((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash-ne $ExpectedHash){throw 'Installed payload hash mismatch; no executable or task will be started.'}
}

$task=Assert-OwnedTask
if($Mode-eq 'Install'){
    if((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $data) -or $task){throw 'Lab installation/profile already exists. Nothing overwritten. Use Configure/Start/Stop/Status as appropriate.'}
    Write-Host 'EXPERIMENTAL: this enables an authenticated SYSTEM remote-control endpoint on the physical console, including the login desktop.'
    Write-Host 'Use only in your test VM after a snapshot. It does NOT change UAC/SAS policies or existing CSIViewer installations.'
    if((Read-Host 'Type ACTIVAR to install and configure the VM-only lab')-cne 'ACTIVAR'){throw 'Cancelled; nothing changed.'}
    $payload=Join-Path $PSScriptRoot 'payload'
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw|ConvertFrom-Json
    foreach($entry in $manifest){
        $relative=[string]$entry.Path
        if([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains(':') -or [IO.Path]::IsPathRooted($relative) -or @($relative-split '[\\/]'|Where-Object {$_-in @('..','.')}).Count){throw 'Invalid payload manifest path.'}
        $source=Join-Path $payload $relative
        if((Get-Item -LiteralPath $source).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Redirected payload refused.'}
        if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash-ne $entry.SHA256){throw 'Payload hash mismatch; nothing installed.'}
    }
    New-SecureDirectory $install
    foreach($entry in $manifest){
        $destination=Join-Path $install $entry.Path
        Copy-SecureFile (Join-Path $payload $entry.Path) $destination $entry.SHA256
    }
    Invoke-Lab '--configure'
    $Mode='Start'
}elseif($Mode-eq 'Configure'){
    Invoke-Lab '--configure'
    Write-Host 'Configured. Start explicitly with -Mode Start -ConfirmVm when ready.'
    return
}

if($Mode-eq 'Start'){
    if($task -and $task.State-eq 'Running'){
        Invoke-Lab '--disable'
        Start-Sleep -Seconds 6
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    Invoke-Lab '--enable'
    if(-not $task){
        $action=New-ScheduledTaskAction -Execute $exe -Argument '--broker'
        $trigger=New-ScheduledTaskTrigger -AtStartup
        $account=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $account -Settings $settings -Description 'Experimental VMware-only CSIViewer machine login endpoint'|Out-Null
    }
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Started the LAB only. After a few seconds use -Mode Status -ConfirmVm to read its NEW machine ID."
    Write-Host 'Connect with a viewer using that new ID and the remote-access password you just configured. Never send anyone your Windows password.'
}elseif($Mode-in @('Stop','RemoveTask')){
    Invoke-Lab '--disable'
    Start-Sleep -Seconds 6
    if($task){Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue}
    if($Mode-eq 'RemoveTask' -and $task){Unregister-ScheduledTask -TaskName $taskName -Confirm:$false}
    Write-Host 'Lab access stopped. Files and machine identity retained; CSIViewerHost and user applications were not changed.'
}elseif($Mode-eq 'Status'){
    foreach($name in @('status.json','worker-failure.json','failure.json')){
        $path=Join-Path $data $name
        if(Test-Path -LiteralPath $path){Write-Host $name;Get-Content -LiteralPath $path}
    }
    if($task){Get-ScheduledTaskInfo -TaskName $taskName|Select-Object LastRunTime,LastTaskResult}
}
