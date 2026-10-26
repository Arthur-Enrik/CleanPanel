; ==============================================================================
; SCRIPT DO INNO SETUP COM VERSIONAMENTO DINÂMICO
; ==============================================================================

; Se a versão não for passada via linha de comando (/DMyAppVersion=X.X.X), usa fallback:
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

; Se o caminho de build não for passado via linha de comando, usa fallback local:
#ifndef MyBuildFolder
  #define MyBuildFolder "..\publish"
#endif

#define MyAppName "CleanPanel"
#define MyAppPublisher "CleanPanel Contributors"
#define MyAppExeName "CleanPanel.exe"
#define MyAppIcon "..\Assets\app.ico"
; Mantenha o GUID existente ou gere um se não houver:
#define MyAppId "{{83FE1830-8C9E-4728-8413-3C495EC4B3BB}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; Força o instalador e o painel do Windows a exibirem apenas "CleanPanel" (sem "versão X.X.X")
AppVerName={#MyAppName}
UninstallDisplayName={#MyAppName}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; Define o ícone do instalador (.exe gerado) e o ícone na lista de Apps Instalados do Windows
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\app.ico
OutputDir=Output
OutputBaseFilename={#MyAppName}_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyBuildFolder}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Copia o app.ico para a pasta de instalação para garantir que os atalhos sempre tenham o ícone
Source: "{#MyAppIcon}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent