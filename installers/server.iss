; Instalador de CSIViewer Servidor.
; Permite instalar para el usuario actual o para todos los usuarios.
; En modo "todos los usuarios" se puede instalar además el servicio de Windows.

#ifndef MyVersion
  #define MyVersion "1.0.1"
#endif

#define MyAppName "CSIViewer Servidor"
#define MyAppExeName "CSIViewerServidor.exe"
#define MyAppPublisher "CSIViewer"

[Setup]
AppId={{84D82658-028E-450A-9266-D0C86A01CAAA}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppVerName={#MyAppName} {#MyVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 CSIViewer
DefaultDirName={autopf}\CSIViewer Servidor
DefaultGroupName=CSIViewer
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=CSIViewerServidor-Setup-{#MyVersion}
SetupIconFile=..\src\CSIViewer.Server\Assets\Appicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
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
CloseApplications=yes
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
Name: "server_service"; Description: "Instalar CSIViewer Servidor como servicio de Windows (solo todos los usuarios)"; GroupDescription: "Servicio"; Flags: unchecked; Check: IsAdminInstallMode

[Files]
Source: "..\artifacts\installers\payload\server\*"; DestDir: "{app}"; Excludes: "*.pdb,data,logs,*.log,*.db,*.db-shm,*.db-wal,serversettings.json,settings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install-server-service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\prepare-server-data.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CSIViewer\CSIViewer Servidor"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Servidor de señalización y relay de CSIViewer"
Name: "{autodesktop}\CSIViewer Servidor"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\prepare-server-data.ps1"" -DataDirectory ""{commonappdata}\CSIViewerServidor"""; StatusMsg: "Preparando los permisos de la base de datos..."; Flags: runhidden waituntilterminated; Check: IsAdmin
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-server-service.ps1"" -ServiceExe ""{app}\CSIViewerServidor.exe"" -Port ""{code:GetServerPort}"" -EnrollmentCode ""{code:GetEnrollmentCode}"" -DatabasePath ""{commonappdata}\CSIViewerServidor\server.db"""; StatusMsg: "Instalando el servicio CSIViewer Servidor..."; Flags: runhidden waituntilterminated; Tasks: server_service; Check: IsAdminInstallMode
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,CSIViewer Servidor}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: ShouldRunServerUi

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-server-service.ps1"" -Uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveServerService"; Check: IsAdmin
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CSIViewerServidor' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveServerStartup"

[Code]
var
  ServerPage: TInputQueryWizardPage;

procedure StopServerProcesses;
var
  ResultCode: Integer;
begin
  // Stop the Windows service first so it releases the executable, then close
  // the administration window (it stays in the notification area).
  Exec('sc.exe', 'stop CSIViewerServidor', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Exec('taskkill.exe', '/IM "CSIViewerServidor.exe" /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent then
  begin
    StopServerProcesses;
    Exit;
  end;

  if MsgBox('Para actualizar CSIViewer Servidor se detendrá el servicio y se cerrará la ventana del servidor. ¿Continuar?',
            mbConfirmation, MB_OKCANCEL) = IDOK then
    StopServerProcesses
  else
    Result := 'Instalación cancelada. Detén el servicio y cierra CSIViewer Servidor antes de volver a intentarlo.';
end;

procedure InitializeWizard;
begin
  ServerPage := CreateInputQueryPage(wpSelectTasks,
    'Servicio de Windows',
    'Configuración del servicio CSIViewer Servidor',
    'Selecciona un código de inscripción fuerte y el puerto. ' +
    'El código debe tener entre 12 y 64 caracteres: letras, números, punto, guion o guion bajo.');
  ServerPage.Add('Código de inscripción:', True);
  ServerPage.Add('Puerto:', False);
  ServerPage.Values[0] := '';
  ServerPage.Values[1] := '5080';
end;

function IsSafeCode(const Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  if (Length(Value) < 12) or (Length(Value) > 64) then
    Exit;

  for I := 1 to Length(Value) do
  begin
    if Pos(Copy(Value, I, 1), 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.') = 0 then
      Exit;
  end;

  Result := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ServerPage.ID) and not WizardIsTaskSelected('server_service');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
begin
  Result := True;
  if CurPageID = ServerPage.ID then
  begin
    if not IsSafeCode(Trim(ServerPage.Values[0])) then
    begin
      MsgBox('El código de inscripción debe tener entre 12 y 64 caracteres y solo puede contener letras, números, punto, guion o guion bajo.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    Port := StrToIntDef(Trim(ServerPage.Values[1]), 0);
    if (Port < 1024) or (Port > 65535) then
    begin
      MsgBox('El puerto debe estar entre 1024 y 65535.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function GetEnrollmentCode(Param: String): String;
begin
  Result := Trim(ServerPage.Values[0]);
end;

function GetServerPort(Param: String): String;
begin
  Result := Trim(ServerPage.Values[1]);
end;

function ShouldRunServerUi: Boolean;
begin
  Result := not WizardIsTaskSelected('server_service');
end;
