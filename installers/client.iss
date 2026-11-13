; Instalador de CSIViewer.
; Permite instalar para el usuario actual o para todos los usuarios.

#ifndef MyVersion
  #define MyVersion "1.0.1"
#endif

#define MyAppName "CSIViewer"
#define MyAppExeName "CSIViewer.exe"
#define MyAppPublisher "CSIViewer"

[Setup]
AppId={{B613C5A8-10AE-483C-8D43-6D304AF325FB}
AppName={#MyAppName}
AppVersion={#MyVersion}
AppVerName={#MyAppName} {#MyVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 CSIViewer
DefaultDirName={autopf}\CSIViewer
DefaultGroupName=CSIViewer
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=CSIViewer-Setup-{#MyVersion}
SetupIconFile=..\src\CSIViewer\Assets\Appicon.ico
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

[Files]
Source: "..\artifacts\installers\payload\client\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\CSIViewer\Client"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey; Check: not IsAdminInstallMode
Root: HKLM64; Subkey: "Software\CSIViewer\Client"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey; Check: IsAdminInstallMode

[Icons]
Name: "{autoprograms}\CSIViewer\CSIViewer"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Asistencia remota CSIViewer"
Name: "{autodesktop}\CSIViewer"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,CSIViewer}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CSIViewer' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveClientStartup"
