# Tests the VM diagnostic setup with mocked task/ACL APIs and temporary files.
# Never installs/starts a scheduled task or invokes the real collector.
$ErrorActionPreference='Stop'
$sourcePath=Join-Path $PSScriptRoot 'test-login-vmware.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($sourcePath,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Collector parse failure'}
$setup=$ast.Find({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Extent.Text.StartsWith("if(`$Mode-ne 'Collect')")},$true)
if(-not $setup){throw 'Setup block missing'}
$setupBlock=[scriptblock]::Create($setup.Extent.Text.Replace('$PSCommandPath','$sourcePath'))
$directory=Join-Path $env:LOCALAPPDATA ('Temp/opencode/login packaging '+[Guid]::NewGuid().ToString('N'))
$taskName='CSIViewerLoginDiagnostic'
$installedScript=Join-Path $directory 'test-login-vmware.ps1'
$launcher=Join-Path $directory 'run-login-probe.cmd'
$powerShell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$commandProcessor=Join-Path $env:WINDIR 'System32/cmd.exe'
$launcherArguments='/d /c ""'+$launcher+'""'
$isAdmin=$true;$Minutes=1;$script:fakeTask=$null
$RunNow=$false;$script:started=$false
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$script:diagnosticAcl=$null
function Get-ScheduledTask {param($TaskName,$ErrorAction) return $script:fakeTask}
function Set-Acl {param($LiteralPath,$AclObject) if($LiteralPath-eq $directory){$script:diagnosticAcl=$AclObject}}
function Get-Acl {param($LiteralPath) return $script:diagnosticAcl}
function New-ScheduledTaskAction {param($Execute,$Argument) return [pscustomobject]@{Execute=$Execute;Arguments=$Argument}}
function New-ScheduledTaskTrigger {param([switch]$AtStartup) return [pscustomobject]@{AtStartup=$AtStartup.IsPresent}}
function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel) return [pscustomobject]@{UserId=$UserId}}
function New-ScheduledTaskSettingsSet {param($ExecutionTimeLimit,$MultipleInstances,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries) return [pscustomobject]@{Limit=$ExecutionTimeLimit}}
function Register-ScheduledTask {
    param($TaskName,$Action,$Trigger,$Principal,$Settings,$Description,[switch]$Force)
    $script:fakeTask=[pscustomobject]@{Actions=@($Action);Principal=$Principal;State='Ready'}
}
function Unregister-ScheduledTask {param($TaskName,$Confirm) $script:fakeTask=$null}
function Start-ScheduledTask {param($TaskName) if($TaskName-ne 'CSIViewerLoginDiagnostic'){throw 'Unexpected task name'};$script:started=$true}
function Check([string]$name,[bool]$value){if(-not $value){throw "Failed: $name"};Write-Host "$name=True"}

$Mode='InstallBootProbe'
& $setupBlock
Check 'installs-mocked-cmd-action' ($script:fakeTask.Actions[0].Execute-eq $commandProcessor)
Check 'per-process-remotesigned-not-bypass' ((Get-Content -LiteralPath $launcher -Raw)-match '-ExecutionPolicy RemoteSigned -File')
Check 'copies-current-collector' ((Get-FileHash $installedScript).Hash-eq (Get-FileHash $sourcePath).Hash)

# Run only synthetic scripts through the generated launcher, not the collector.
"throw 'Synthetic bootstrap failure'"|Set-Content -LiteralPath $installedScript -Encoding ASCII
& $commandProcessor /d /c "`"$launcher`""
$failureCode=$LASTEXITCODE
$log=Get-Content -LiteralPath (Join-Path $directory 'boot-startup.log') -Raw
Check 'startup-failure-is-visible' ($failureCode-eq 1 -and $log.Contains('Synthetic bootstrap failure') -and $log.Contains('ExitCode=1'))
"Write-Output 'SYNTHETIC_OK'"|Set-Content -LiteralPath $installedScript -Encoding ASCII
& $commandProcessor /d /c "`"$launcher`""
Check 'startup-success-is-visible' ($LASTEXITCODE-eq 0 -and (Get-Content -LiteralPath (Join-Path $directory 'boot-startup.log') -Raw).Contains('ExitCode=0'))

$Mode='RepairBootProbe'
& $setupBlock
Check 'repairs-owned-diagnostic' ((Get-FileHash $installedScript).Hash-eq (Get-FileHash $sourcePath).Hash)
$script:fakeTask.Actions[0]=[pscustomobject]@{Execute=$powerShell;Arguments="-NoProfile -NonInteractive -File `"$installedScript`" -Mode Collect -Minutes 5 -ConfirmVm"}
& $setupBlock
Check 'repairs-previous-powershell-action' ($script:fakeTask.Actions[0].Execute-eq $commandProcessor)
$legacyAcl=New-Object Security.AccessControl.DirectorySecurity
$legacyAcl.SetAccessRuleProtection($true,$false)
$legacyAcl.SetOwner($identity.User)
$legacyAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User,'FullControl','Allow')))
$script:diagnosticAcl=$legacyAcl
& $setupBlock
Check 'accepts-only-current-elevated-creator-ace' ($script:diagnosticAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value-eq 'S-1-5-32-544')
Check 'removes-legacy-user-ace-from-new-dacl' (@($script:diagnosticAcl.Access|Where-Object {$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value-notin @('S-1-5-18','S-1-5-32-544')}).Count-eq 0)
$adminOwnerAcl=New-Object Security.AccessControl.DirectorySecurity
$adminOwnerAcl.SetSecurityDescriptorSddlForm('O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
$adminOwnerAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User,'FullControl','Allow')))
$script:diagnosticAcl=$adminOwnerAcl
& $setupBlock
Check 'accepts-current-creator-ace-with-administrators-owner' ($script:diagnosticAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value-eq 'S-1-5-32-544')
$unknownAcl=New-Object Security.AccessControl.DirectorySecurity
$unknownAcl.SetAccessRuleProtection($true,$false)
$unknownAcl.SetOwner($identity.User)
$unknownAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-1-0')),'FullControl','Allow')))
$script:diagnosticAcl=$unknownAcl
$refused=$false
try {& $setupBlock}catch{$refused=$true}
Check 'still-refuses-unrelated-account-ace' $refused
$wrongOwnerAcl=New-Object Security.AccessControl.DirectorySecurity
$wrongOwnerAcl.SetAccessRuleProtection($true,$false)
$wrongOwnerAcl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-19')))
$wrongOwnerAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity.User,'FullControl','Allow')))
$script:diagnosticAcl=$wrongOwnerAcl
$refused=$false
try {& $setupBlock}catch{$refused=$true}
Check 'refuses-creator-ace-with-different-owner' $refused
$script:fakeTask.Actions[0].Arguments='unrelated-command'
$refused=$false
try {& $setupBlock}catch{$refused=$true}
Check 'refuses-unrelated-task' $refused
$script:fakeTask.Actions[0].Arguments=$launcherArguments
$script:diagnosticAcl=New-Object Security.AccessControl.DirectorySecurity
$script:diagnosticAcl.SetSecurityDescriptorSddlForm('O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)',[Security.AccessControl.AccessControlSections]'Access,Owner')
$RunNow=$true
& $setupBlock
Check 'explicit-runnow-starts-only-owned-diagnostic' $script:started
Write-Host '14/14 packaging checks passed. No real tasks, policies or ACLs changed.'
