param(
    [string]$Version = "1.1.0",
    [string]$Configuration = "Release"
)

# 1. Diretórios absolutos baseados na pasta onde o script está
$InstallerDir = $PSScriptRoot
if (-not $InstallerDir) { $InstallerDir = Get-Location }
$ProjectRoot  = (Resolve-Path "$InstallerDir\..").Path
$IssFile      = Join-Path $InstallerDir "setup.iss"
$OutputDir    = Join-Path $InstallerDir "Output"
$PublishDir   = Join-Path $ProjectRoot "publish"
$CsprojFile   = Join-Path $ProjectRoot "CleanPanel.csproj"

# 2. Localiza o ISCC.exe (Busca profunda)
Write-Host "Verificando instalação do Inno Setup..." -ForegroundColor Cyan

$IsccExe = $null

# 2.1. Verifica no PATH do sistema
$pathCheck = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($pathCheck) {
    $IsccExe = $pathCheck.Source
}

# 2.2. Se não estiver no PATH, busca nas chaves do Registro do Windows (HKLM e HKCU)
if (-not $IsccExe) {
    $regKeys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1"
    )

    foreach ($key in $regKeys) {
        if (Test-Path $key) {
            $installLocation = (Get-ItemProperty -Path $key -Name "InstallLocation" -ErrorAction SilentlyContinue).InstallLocation
            if ($installLocation -and (Test-Path "$installLocation\ISCC.exe")) {
                $IsccExe = Join-Path $installLocation "ISCC.exe"
                break
            }
        }
    }
}

# 2.3. Se não encontrar no Registro, varre caminhos conhecidos (AppData, Program Files, Chocolatey, Scoop)
if (-not $IsccExe) {
    $commonPaths = @(
        # AppData (Instalação por usuário / sem admin)
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe",
        "$env:APPDATA\Inno Setup 6\ISCC.exe",
        "$env:APPDATA\Inno Setup 5\ISCC.exe",
        
        # Program Files (Instalação global / todos os usuários)
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
        "C:\Program Files\Inno Setup 5\ISCC.exe",
        
        # Gerenciadores de pacotes
        "C:\ProgramData\chocolatey\bin\ISCC.exe",
        "$env:USERPROFILE\scoop\shims\ISCC.exe",
        "$env:USERPROFILE\scoop\apps\inno-setup\current\ISCC.exe"
    )

    $IsccExe = $commonPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# 2.4. Validação final
if (-not $IsccExe) {
    Write-Host ""
    Write-Host "==========================================================================" -ForegroundColor Red
    Write-Host "[ERRO] O compilador do Inno Setup (ISCC.exe) não foi encontrado no sistema!" -ForegroundColor Red
    Write-Host "Ele não está no PATH, Registro, AppData ou Arquivos de Programas." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Para instalar rapidamente via terminal, execute:" -ForegroundColor Yellow
    Write-Host "  winget install JRSoftware.InnoSetup" -ForegroundColor White
    Write-Host "==========================================================================" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Inno Setup encontrado em: $IsccExe" -ForegroundColor Green
Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Preparando Instalador CleanPanel v$Version" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 3. Compila e publica o projeto C# se a pasta publish não existir
if (-not (Test-Path $PublishDir)) {
    Write-Host "[1/2] Gerando binários com dotnet publish..." -ForegroundColor Yellow
    dotnet publish $CsprojFile -c $Configuration -o $PublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERRO] Falha ao publicar o projeto C#." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[1/2] Pasta publish encontrada: $PublishDir" -ForegroundColor Green
}

# 4. Executa o Inno Setup exibindo a saída no console
Write-Host "[2/2] Compilando instalador Inno Setup..." -ForegroundColor Yellow

$arguments = @(
    "/DMyAppVersion=$Version",
    "/DMyBuildFolder=$PublishDir",
    "$IssFile"
)

& $IsccExe $arguments

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "[SUCESSO] Instalador v$Version gerado em: $OutputDir" -ForegroundColor Green
    Write-Host "=============================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Red
    Write-Host "[ERRO] Falha ao compilar o instalador. Verifique as mensagens acima." -ForegroundColor Red
    Write-Host "=============================================" -ForegroundColor Red
    exit $LASTEXITCODE
}