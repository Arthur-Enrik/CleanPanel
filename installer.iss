; ==============================================================================
; SCRIPT DE INSTALAÇÃO INNO SETUP - CLEANPANEL
; ==============================================================================
; Compilado via build-installer.ps1 (repassando versão e diretórios via /D).
; Também suporta compilação direta pelo compilador Inno Setup usando fallbacks.
; ==============================================================================

; ------------------------------------------------------------------------------
; 1. CONSTANTES DE IDENTIDADE E DIRETIVAS DINÂMICAS
; ------------------------------------------------------------------------------
#define MyAppName      "Cleanpanel"
#define MyAppPublisher "Cleanpanel"
#define MyAppURL       "https://github.com/Arthur-Enrik/CleanPanel"
#define MyAppExeName   "Cleanpanel.exe"
#define MyAppIcon      "Assets\app.ico"
; Identificador único preservado para atualizações in-place sem duplicar entradas no Windows
#define MyAppId        "{{83FE1830-8C9E-4728-8413-3C495EC4B3BB}}"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "1.0.0.0"
#endif

#ifndef MyBuildFolder
  #define MyBuildFolder "publish"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "installer\Output"
#endif

; ------------------------------------------------------------------------------
; 2. CONFIGURAÇÕES GERAIS DO INSTALADOR ([Setup])
; ------------------------------------------------------------------------------
[Setup]
; Identidade e metadados exibidos estritamente como "Cleanpanel" no Windows
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
UninstallDisplayName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppNumericVersion}
VersionInfoDescription={#MyAppName} Installer

; Diretórios, arquitetura x64 exclusiva e privilégios administrativos
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UsedUserAreasWarning=no
CloseApplications=yes
RestartApplications=no

; Saída e compressão otimizada LZMA2
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}_Setup_{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

; Aparência moderna e ícones oficiais
WizardStyle=modern
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}

; ------------------------------------------------------------------------------
; 3. IDIOMAS ([Languages])
; ------------------------------------------------------------------------------
[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; ------------------------------------------------------------------------------
; 4. TAREFAS OPCIONAIS ([Tasks])
; ------------------------------------------------------------------------------
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ------------------------------------------------------------------------------
; 5. ARQUIVOS DO APLICATIVO ([Files])
; ------------------------------------------------------------------------------
[Files]
Source: "{#MyBuildFolder}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildFolder}\*"; DestDir: "{app}"; Excludes: "{#MyAppExeName},settings.json,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyAppIcon}"; DestDir: "{app}"; Flags: ignoreversion

; ------------------------------------------------------------------------------
; 6. ATALHOS NO MENU INICIAR E ÁREA DE TRABALHO ([Icons])
; ------------------------------------------------------------------------------
[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

; ------------------------------------------------------------------------------
; 7. EXECUÇÃO PÓS-INSTALAÇÃO ([Run])
; ------------------------------------------------------------------------------
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; ------------------------------------------------------------------------------
; 8. LIMPEZA COMPLETA NA DESINSTALAÇÃO ([UninstallDelete])
; ------------------------------------------------------------------------------
[UninstallDelete]
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.log"
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\Cleanpanel"
Type: filesandordirs; Name: "{localappdata}\CleanPanel"
Type: filesandordirs; Name: "{userappdata}\Cleanpanel"
Type: filesandordirs; Name: "{userappdata}\CleanPanel"

; ------------------------------------------------------------------------------
; 9. PASCAL SCRIPT DE CICLO DE VIDA DA INSTALAÇÃO E DESINSTALAÇÃO ([Code])
; ------------------------------------------------------------------------------
[Code]
const
  StartupTaskName       = 'CleanPanelAutoStart';
  LegacyStartupTaskName = 'HardwareOverlayMonitorApp';

// Executa um comando do sistema de forma silenciosa (janela oculta) aguardando o término.
function RunSilentCommand(const ExecutablePath, Params: String; var ResultCode: Integer): Boolean;
begin
  Result := Exec(ExecutablePath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Encerra qualquer instância em execução do aplicativo (Windows é case-insensitive no nome da imagem).
function TerminateRunningInstances(): Boolean;
var
  ResultCode: Integer;
begin
  Result := RunSilentCommand(
    ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM {#MyAppExeName} /T',
    ResultCode) and (ResultCode = 0);
end;

// Fecha instâncias ativas antes de sobrescrever binários em atualizações/reinstalações.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if TerminateRunningInstances() then
  begin
    Sleep(300);
  end;

  Result := '';
end;

// Encerra processos ativos do aplicativo antes de iniciar a remoção de arquivos.
function InitializeUninstall(): Boolean;
begin
  if TerminateRunningInstances() then
  begin
    Sleep(300);
  end;

  Result := True;
end;

// Remove tarefas agendadas de auto-start e garante a exclusão integral do diretório residual.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    RunSilentCommand(
      ExpandConstant('{sys}\schtasks.exe'),
      '/Delete /TN "' + StartupTaskName + '" /F',
      ResultCode);

    RunSilentCommand(
      ExpandConstant('{sys}\schtasks.exe'),
      '/Delete /TN "' + LegacyStartupTaskName + '" /F',
      ResultCode);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
    begin
      DelTree(AppDir, True, True, True);
    end;
  end;
end;
