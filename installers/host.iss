; Instalador de CSIViewer Host (modo desatendido).
; Incluye solo CSIViewer Host; la ventana de host usa el cliente instalado aparte.

#ifndef MyVersion
  #define MyVersion "1.0.1"
#endif

#define MyAppName "CSIViewer Host"
#define MyAppExeName "CSIViewer.Host.exe"
#define MyAppPublisher "CSIViewer"

[Setup]
AppId={{962C19F9-268D-4DD8-84EF-0AC0893C13E4}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppVerName={#MyAppName} {#MyVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 CSIViewer
DefaultDirName={autopf}\CSIViewer Host
DefaultGroupName=CSIViewer
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=CSIViewer.Host-Setup-{#MyVersion}
SetupIconFile=..\src\CSIViewer.Host\Assets\Appicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=CSIViewer Host
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupLogging=yes
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
VersionInfoVersion={#MyVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Instalador de {#MyAppName}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "host_service"; Description: "Iniciar CSIViewer Host al arrancar Windows (tarea programada, todos los usuarios)"; GroupDescription: "Inicio automático"; Check: IsAdminInstallMode

[Files]
Source: "..\artifacts\installers\payload\host\ui\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\installers\payload\host\agent\*"; DestDir: "{app}\agent"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install-host-autostart.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CSIViewer\CSIViewer Host"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Ventana de anfitrión de CSIViewer"
Name: "{autodesktop}\CSIViewer Host"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-host-autostart.ps1"" -ServiceExe ""{app}\agent\CSIViewer.Host.Agent.exe"""; StatusMsg: "Instalando el inicio automático de CSIViewer Host..."; Flags: runhidden waituntilterminated; Tasks: host_service; Check: IsAdminInstallMode

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-host-autostart.ps1"" -Uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveHostService"; Check: IsAdmin

[Code]
procedure StopHostProcesses;
var
  ResultCode: Integer;
begin
  // Stop the service first so it releases the service binaries.
  Exec('sc.exe', 'stop CSIViewerHost', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('schtasks.exe', '/End /TN CSIViewerHost', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1200);
  // The UI can be minimized to the notification area; close it explicitly.
  Exec('taskkill.exe', '/IM "CSIViewer.Host.exe" /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM "CSIViewer.Host.Agent.exe" /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM "CSIViewer.Host.Service.exe" /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent then
  begin
    StopHostProcesses;
    Exit;
  end;

  if MsgBox('Para actualizar CSIViewer Host se cerrará la ventana del host y se detendrá el inicio automático. ¿Continuar?',
            mbConfirmation, MB_OKCANCEL) = IDOK then
    StopHostProcesses
  else
    Result := 'Instalación cancelada. Cierra CSIViewer Host y detén el inicio automático antes de volver a intentarlo.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // El inicio automático es opcional: puede activarse después desde
  // Configuración > Modo desatendido, así que no se muestra ningún aviso.
end;
