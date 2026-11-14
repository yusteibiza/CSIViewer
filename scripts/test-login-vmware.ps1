param(
    [ValidateSet('Collect','InstallBootProbe','RepairBootProbe','RemoveBootProbe')][string]$Mode='Collect',
    [ValidateRange(1,10)][int]$Minutes=5,
    [switch]$ConfirmVm,
    [switch]$RunNow
)

# Diagnostic only: no capture, input injection, passwords, OS-policy changes,
# CSIViewer installation changes or automatic reboot. Run INSIDE the test VM.
$ErrorActionPreference='Stop'
$machine=Get-CimInstance Win32_ComputerSystem
if(-not $ConfirmVm -or "$($machine.Manufacturer) $($machine.Model)" -notmatch 'VMware'){
    throw 'Run this diagnostic inside the VMware test VM and pass -ConfirmVm. Nothing changed.'
}
if($RunNow -and $Mode-notin @('InstallBootProbe','RepairBootProbe')){throw 'RunNow is allowed only for InstallBootProbe or RepairBootProbe.'}
$taskName='CSIViewerLoginDiagnostic'
$directory=Join-Path $env:ProgramData 'CSIViewerLoginDiagnostic'
$installedScript=Join-Path $directory 'test-login-vmware.ps1'
$launcher=Join-Path $directory 'run-login-probe.cmd'
$powerShell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$commandProcessor=Join-Path $env:WINDIR 'System32/cmd.exe'
$launcherArguments='/d /c ""'+$launcher+'""'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if($Mode-ne 'Collect'){
    if(-not $isAdmin){throw 'Open PowerShell as administrator inside the VM.'}
    $existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if($existing){
        $actions=@($existing.Actions)
        if($actions.Count-ne 1){throw 'Unexpected diagnostic task actions. Nothing changed.'}
        $oldArguments='^-NoProfile -NonInteractive -File "'+[regex]::Escape($installedScript)+'" -Mode Collect -Minutes (?:[1-9]|10) -ConfirmVm$'
        $oldAction=$actions[0].Execute-eq $powerShell -and $actions[0].Arguments-match $oldArguments
        $newAction=$actions[0].Execute-eq $commandProcessor -and $actions[0].Arguments-eq $launcherArguments
        if((-not $oldAction -and -not $newAction) -or $existing.Principal.UserId-notin @('SYSTEM','S-1-5-18')){
            throw 'A task with this name exists and is not owned by this diagnostic. Refusing to change it.'
        }
    }
    if($Mode-eq 'RemoveBootProbe'){
        if($existing){Unregister-ScheduledTask -TaskName $taskName -Confirm:$false}
        Write-Host 'Diagnostic boot task removed. Reports retained. CSIViewer tasks/services were not changed.'
        return
    }
    if($Mode-eq 'RepairBootProbe'){
        if(-not $existing -or -not (Test-Path -LiteralPath $directory)){throw 'Owned diagnostic task/directory missing; repair refused.'}
        if($existing.State-eq 'Running'){throw 'Diagnostic task is running; wait until it finishes before repairing it.'}
        foreach($path in @($directory,$installedScript,$launcher,(Join-Path $directory 'boot-startup.log'))){
            if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes-band [IO.FileAttributes]::ReparsePoint)){
                throw 'Reparse point in diagnostic path; repair refused.'
            }
        }
        $oldAcl=Get-Acl -LiteralPath $directory
        if(-not $oldAcl.AreAccessRulesProtected){throw 'Diagnostic directory permissions are not protected; repair refused.'}
        $creatorSid=$identity.User.Value
        $ownerSid=$oldAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        foreach($rule in $oldAcl.Access){
            if($rule.AccessControlType-eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value-notin @('S-1-5-18','S-1-5-32-544')){
                $ruleSid=$rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
                # Accept the legacy explicit creator ACE only for this elevated
                # caller, with ownership by that caller or Administrators.
                if($ruleSid-ne $creatorSid -or $ownerSid-notin @($creatorSid,'S-1-5-32-544') -or $rule.IsInherited){
                    throw 'Unexpected permissions in diagnostic directory; repair refused.'
                }
                Write-Host 'Recognized legacy creator permission for the current elevated administrator.'
            }
        }
    }else{
        if($existing -or (Test-Path -LiteralPath $directory)){throw 'Diagnostic already exists. Use RepairBootProbe to repair the owned diagnostic only.'}
        $null=New-Item -ItemType Directory -Path $directory
    }
    $acl=New-Object Security.AccessControl.DirectorySecurity
    # Set an exact protected DACL and administrator-group ownership, rather
    # than retaining a legacy creator ACE on the SYSTEM diagnostic directory.
    $acl.SetSecurityDescriptorSddlForm('O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
    Set-Acl -LiteralPath $directory -AclObject $acl
    if([IO.Path]::GetFullPath($PSCommandPath)-ne [IO.Path]::GetFullPath($installedScript)){
        Copy-Item -LiteralPath $PSCommandPath -Destination $installedScript -Force
    }
    # A tiny cmd launcher logs even PowerShell startup, execution-policy and
    # script-parse failures, which occur before the collector can write JSONL.
    # RemoteSigned applies to THIS diagnostic process only, not a stored policy.
    # MachinePolicy/UserPolicy still take precedence. Never use Bypass/Unrestricted.
    $startupLog=Join-Path $directory 'boot-startup.log'
    @"
@echo off
setlocal
> "$startupLog" echo CSIViewer VM login diagnostic starting
"$powerShell" -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "$installedScript" -Mode Collect -Minutes $Minutes -ConfirmVm >> "$startupLog" 2>&1
set "PROBE_EXIT=%ERRORLEVEL%"
>> "$startupLog" echo ExitCode=%PROBE_EXIT%
exit /b %PROBE_EXIT%
"@|Set-Content -LiteralPath $launcher -Encoding ASCII
    foreach($filePath in @($installedScript,$launcher,$startupLog)){
        if(Test-Path -LiteralPath $filePath){
            $fileAcl=New-Object Security.AccessControl.FileSecurity
            $fileAcl.SetSecurityDescriptorSddlForm('O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
            Set-Acl -LiteralPath $filePath -AclObject $fileAcl
        }
    }
    $action=New-ScheduledTaskAction -Execute $commandProcessor -Argument $launcherArguments
    $trigger=New-ScheduledTaskTrigger -AtStartup
    $account=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ($Minutes+1)) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $account -Settings $settings -Description 'Temporary read-only CSIViewer login diagnostic for a VMware test VM' -Force|Out-Null
    if($RunNow){
        Start-ScheduledTask -TaskName $taskName
        Write-Host "VM-only read-only diagnostic started now for $Minutes minute(s). No reboot needed."
    }else{
        Write-Host 'VM-only read-only boot probe prepared. It will run on the NEXT reboot; no reboot performed.'
    }
    Write-Host "Reports: $directory. This does NOT implement remote login."
    Write-Host 'PowerShell RemoteSigned is selected for the diagnostic process only; permanent policies are unchanged.'
    return
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class CSIViewerLoginProbe {
    [StructLayout(LayoutKind.Sequential)] struct Session { public int Id; public IntPtr Name; public int State; }
    public sealed class SessionResult { public int Id; public int State; public bool? UserTokenAvailable; public int? Error; }
    public sealed class DesktopResult { public bool Available; public string Name; public int Error; }
    [DllImport("wtsapi32.dll", SetLastError=true)] static extern bool WTSEnumerateSessions(IntPtr s,int r,int v,out IntPtr p,out int n);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);
    [DllImport("wtsapi32.dll", SetLastError=true)] static extern bool WTSQueryUserToken(uint s,out IntPtr t);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr OpenInputDesktop(uint f,bool i,uint a);
    [DllImport("user32.dll", CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr h,int i,StringBuilder b,int l,out int n);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    public static SessionResult[] Sessions(bool system) {
        IntPtr data; int count;
        if(!WTSEnumerateSessions(IntPtr.Zero,0,1,out data,out count)) throw new System.ComponentModel.Win32Exception();
        try {
            var results=new List<SessionResult>();
            for(int i=0;i<count;i++) {
                var item=(Session)Marshal.PtrToStructure(IntPtr.Add(data,i*Marshal.SizeOf(typeof(Session))),typeof(Session));
                var result=new SessionResult {Id=item.Id,State=item.State};
                if(system && item.Id>0) {
                    IntPtr token;
                    result.UserTokenAvailable=WTSQueryUserToken((uint)item.Id,out token);
                    if(result.UserTokenAvailable==true) CloseHandle(token); else result.Error=Marshal.GetLastWin32Error();
                }
                results.Add(result);
            }
            return results.ToArray();
        } finally {WTSFreeMemory(data);}
    }
    public static DesktopResult Desktop() {
        // READOBJECTS only. Never SetThreadDesktop/SwitchDesktop/SendInput.
        var handle=OpenInputDesktop(0,false,1);
        if(handle==IntPtr.Zero) return new DesktopResult {Available=false,Error=Marshal.GetLastWin32Error()};
        try {
            var text=new StringBuilder(256); int needed;
            if(!GetUserObjectInformation(handle,2,text,text.Capacity*2,out needed))
                return new DesktopResult {Available=true,Error=Marshal.GetLastWin32Error()};
            return new DesktopResult {Available=true,Name=text.ToString()};
        } finally {CloseDesktop(handle);}
    }
}
'@

if($identity.IsSystem){
    if(-not (Test-Path -LiteralPath $directory)){throw 'Protected diagnostic directory missing.'}
}else{
    $directory=Join-Path $env:LOCALAPPDATA 'CSIViewerLoginDiagnostic'
    $null=New-Item -ItemType Directory -Path $directory -Force
}
$file=Join-Path $directory ('login-probe-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$PID+'.jsonl')
$os=Get-CimInstance Win32_OperatingSystem
@{Type='Environment';Time=(Get-Date).ToString('o');Windows=$os.Caption;Build=$os.BuildNumber;IsSystem=$identity.IsSystem;ProcessSessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId}|ConvertTo-Json -Compress|Set-Content -LiteralPath $file -Encoding UTF8
$until=(Get-Date).AddMinutes($Minutes)
Write-Host "Collecting read-only samples for $Minutes minute(s). No passwords or screen images are read."
Write-Host "Report: $file. You may lock/unlock the VM manually from the VMware console."
do{
    @{Type='Sample';Time=(Get-Date).ToString('o');Desktop=[CSIViewerLoginProbe]::Desktop();Sessions=@([CSIViewerLoginProbe]::Sessions($identity.IsSystem))}|ConvertTo-Json -Depth 4 -Compress|Add-Content -LiteralPath $file -Encoding UTF8
    Start-Sleep -Seconds 2
}while((Get-Date)-lt $until)
Write-Host "Read-only diagnostic finished: $file"
