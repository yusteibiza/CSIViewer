param([ValidateSet('Manage','Configure','Enable','Disable','Status')][string]$Mode='Manage')
$ErrorActionPreference='Stop'
$root=Join-Path $env:ProgramFiles 'CSIViewer Host'
if([IO.Path]::GetFullPath($PSScriptRoot)-ne [IO.Path]::GetFullPath($root)){throw 'Instala CSIViewer Host para todos los usuarios en la ruta predeterminada.'}
function Assert-Protected([IO.FileSystemInfo]$Item){
    if(-not $Item.Exists -or ($Item.Attributes-band [IO.FileAttributes]::ReparsePoint)){throw 'Ruta de instalacion ausente o redirigida.'}
    $acl=Get-Acl -LiteralPath $Item.FullName
    $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value-notin $trusted){throw 'Propietario de instalacion no seguro.'}
    $write=[Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteAttributes,WriteExtendedAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.PropagationFlags-band [Security.AccessControl.PropagationFlags]::InheritOnly){continue}
        if($rule.AccessControlType-eq 'Allow' -and $rule.IdentityReference.Value-notin $trusted -and ($rule.FileSystemRights-band $write)){
            throw 'La instalacion puede modificarse sin administracion; no se elevara este componente.'
        }
    }
}
Assert-Protected (Get-Item -LiteralPath $env:ProgramFiles)
Assert-Protected (Get-Item -LiteralPath $root)
foreach($path in @($PSCommandPath,(Join-Path $root 'install-host-autostart.ps1'),(Join-Path $root 'agent'),(Join-Path $root 'login'))){
    $item=Get-Item -LiteralPath $path
    Assert-Protected $item
    if($item.PSIsContainer){
        $pending=New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
        $pending.Push($item)
        while($pending.Count-gt 0){
            $directory=$pending.Pop()
            Assert-Protected $directory
            foreach($child in $directory.EnumerateFileSystemInfos()){
                Assert-Protected $child
                if($child-is [IO.DirectoryInfo]){$pending.Push($child)}
            }
        }
    }
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    $powershell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $process=Start-Process -FilePath $powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy RemoteSigned -File `"$PSCommandPath`" -Mode $Mode" -PassThru -Wait
    exit $process.ExitCode
}
$exe=Join-Path $root 'login/CSIViewer.Host.Login.exe'
$data=Join-Path $env:ProgramData 'CSIViewer.Host.Login'
function Invoke-Login([string]$Argument){& $exe $Argument;if($LASTEXITCODE-ne 0){throw 'No se completo la operacion. Revisa el mensaje anterior; no se omiten las protecciones de Windows.'}}
function Show-Status {
    $status=Join-Path $data 'status.json'
    if(Test-Path -LiteralPath $status){Get-Content -LiteralPath $status}else{Write-Host 'Todavia no hay estado de equipo. Configura/habilita el acceso y espera unos segundos.'}
    Write-Host 'Usa MachineId y la clave CSIViewer de equipo, NO el ID/clave del host de usuario.'
}
function Enable-Login {
    Invoke-Login '--enable'
    & (Join-Path $root 'install-host-autostart.ps1') -ServiceExe (Join-Path $root 'agent/CSIViewer.Host.Agent.exe')
    Write-Host 'CSIViewerHost mantiene su agente habitual; el componente de login se inicia solo con configuracion autorizada.'
}
function Execute-Mode([string]$Action){
    switch($Action){
        'Configure'{Invoke-Login '--configure';Enable-Login;Start-Sleep -Seconds 7;Show-Status}
        'Enable'{Enable-Login;Start-Sleep -Seconds 7;Show-Status}
        'Disable'{Invoke-Login '--disable';Write-Host 'Acceso al login deshabilitado. El host y visor de usuario no se modifican.'}
        'Status'{Show-Status}
    }
}
try{
    if($Mode-ne 'Manage'){Execute-Mode $Mode;Read-Host 'Pulsa Enter para cerrar'|Out-Null;return}
    while($true){
        Write-Host "`nAcceso al inicio de Windows (version candidata; validacion de login pendiente)"
        Write-Host '1 Configurar   2 Habilitar   3 Deshabilitar   4 Ver ID/estado   0 Salir'
        switch(Read-Host 'Opcion'){
            '1'{Execute-Mode 'Configure'}
            '2'{Execute-Mode 'Enable'}
            '3'{Execute-Mode 'Disable'}
            '4'{Execute-Mode 'Status'}
            '0'{return}
        }
    }
}catch{Write-Host $_.Exception.Message;Read-Host 'Pulsa Enter para cerrar'|Out-Null;exit 1}
