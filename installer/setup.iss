; ==============================================================================
; SCRIPT DE INSTALAÇÃO INNO SETUP - CLEANPANEL
; Compatível com: installer\build_installer.ps1 e .github\workflows\release.yml
; ==============================================================================

; ------------------------------------------------------------------------------
; 1. CABEÇALHO E CONSTANTES DE VERSÃO / IDENTIDADE
; ------------------------------------------------------------------------------
#define MyAppName      "CleanPanel"
#define MyAppPublisher "CleanPanel Contributors"
#define MyAppURL       "https://github.com/Arthur-Enrik/CleanPanel"
#define MyAppExeName   "CleanPanel.exe"
#define MyAppIcon      "..\Assets\app.ico"
; Identificador único preservado para atualizações in-place sem duplicar entradas no Painel de Controle
#define MyAppId        "{{83FE1830-8C9E-4728-8413-3C495EC4B3BB}}"

; Fallback de versão sincronizado com CleanPanel.csproj quando não informado via /DMyAppVersion
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

; Fallback da pasta de publicação quando não informado via /DMyBuildFolder
#ifndef MyBuildFolder
  #define MyBuildFolder "..\publish"
#endif

; ------------------------------------------------------------------------------
; 2. CONFIGURAÇÕES GERAIS DO INSTALADOR ([Setup])
; ------------------------------------------------------------------------------
[Setup]
; Identidade e metadados exibidos no Windows (Apps e Recursos / Painel de Controle)
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
UninstallDisplayName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Diretórios e comportamento de instalação/atualização
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Privilégios administrativos necessários para driver WinRing0/LibreHardwareMonitor e sessões ETW
PrivilegesRequired=admin
; Permite limpeza preventiva de áreas de usuário em [UninstallDelete] sem emitir aviso do compilador
UsedUserAreasWarning=no
CloseApplications=yes
RestartApplications=no

; Saída e compactação máxima
OutputDir=Output
OutputBaseFilename={#MyAppName}_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

; Aparência moderna e ícones
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
; Garante a disponibilidade física de app.ico na pasta de instalação para os atalhos do Windows
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
; 8. LIMPEZA DE CONFIGURAÇÕES, LOGS E RESÍDUOS NA DESINSTALAÇÃO ([UninstallDelete])
; ------------------------------------------------------------------------------
[UninstallDelete]
; Arquivos gerados em tempo de execução na pasta do aplicativo
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.log"
Type: filesandordirs; Name: "{app}"
; Limpeza preventiva de eventuais diretórios de cache ou dados de usuário
Type: filesandordirs; Name: "{localappdata}\CleanPanel"
Type: filesandordirs; Name: "{userappdata}\CleanPanel"

; ------------------------------------------------------------------------------
; 9. PASCAL SCRIPT DE CICLO DE VIDA DA DESINSTALAÇÃO ([Code])
; ------------------------------------------------------------------------------
[Code]
const
  StartupTaskName       = 'CleanPanelAutoStart';
  LegacyStartupTaskName = 'HardwareOverlayMonitorApp';

// Executa um utilitário de sistema de forma silenciosa (janela oculta) aguardando a conclusão.
function RunSilentCommand(const ExecutablePath, Params: String; var ResultCode: Integer): Boolean;
begin
  Result := Exec(ExecutablePath, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Antes de iniciar a remoção dos arquivos, encerra qualquer instância ativa do CleanPanel
// para evitar erros de "arquivo em uso" nos binários ou handles abertos.
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  if RunSilentCommand(
       ExpandConstant('{sys}\taskkill.exe'),
       '/F /IM {#MyAppExeName} /T',
       ResultCode) and (ResultCode = 0) then
  begin
    // Pequena pausa para garantir liberação de handles do processo encerrado pelo Windows
    Sleep(300);
  end;

  Result := True;
end;

// Executa rotinas adicionais nas etapas da desinstalação:
// - usUninstall: remove as tarefas agendadas de inicialização automática no Windows
// - usPostUninstall: garante a remoção integral do diretório {app} residual
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
