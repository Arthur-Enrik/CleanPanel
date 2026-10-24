; =====================================================================
; Script Inno Setup - CleanPanel (Windows 11 x64)
; =====================================================================

#ifndef MyAppName
  #define MyAppName "CleanPanel"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

#define MyAppPublisher "CleanPanel Contributors"
#define MyAppExeName "CleanPanel.exe"
#define MyPublishDir "Publish"
#define MyOutputDir "Output"

[Setup]
AppId={{8A9D3F54-4C7E-4B18-9F6A-2E7B8C1D5A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename=CleanPanel_Setup
SetupIconFile=Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
LicenseFile=LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.LaunchAppNow=Executar {#MyAppName} agora
english.LaunchAppNow=Launch {#MyAppName} now

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Marca o executavel instalado para abrir sempre como Administrador no Windows (para todos os usuarios)
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\{#MyAppExeName}"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; runascurrentuser + shellexec garante que ao finalizar o instalador o app herde o token de Administrador do Setup
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAppNow}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent shellexec runascurrentuser

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""CleanPanelAutoStart"" /F"; Flags: runhidden; RunOnceId: "RemoveCleanPanelStartupTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""HardwareOverlayMonitorApp"" /F"; Flags: runhidden; RunOnceId: "RemoveLegacyStartupTask"

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
Type: dirifempty; Name: "{app}"
