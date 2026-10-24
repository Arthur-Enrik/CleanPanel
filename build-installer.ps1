<#
.SYNOPSIS
    Script automatizado de Build, Publish (.NET 8 Self-Contained) e Empacotamento do Instalador (.exe) do CleanPanel via Inno Setup.
.DESCRIPTION
    1. Localiza o compilador do Inno Setup (ISCC.exe) garantindo retorno de string unica.
    2. Valida o cabecalho binario Win32 ICO (00 00 01 00) de Assets\app.ico para evitar corrupcao PE (0x800700C1).
    3. Limpa os diretorios temporarios de publicacao (./Publish) e saida (./Output).
    4. Executa o dotnet publish (Release, win-x64, self-contained=true, PublishSingleFile=false) para CleanPanel.csproj.
    5. Compila o script installer.iss via ISCC.exe gerando Output\CleanPanel_Setup.exe.
#>

$ErrorActionPreference = "Stop"

# Garante que o diretorio de trabalho seja a pasta onde o script esta localizado
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

$CsprojPath = Join-Path $ProjectRoot "CleanPanel.csproj"
$IssPath    = Join-Path $ProjectRoot "installer.iss"
$IcoPath    = Join-Path $ProjectRoot "Assets\app.ico"
$PublishDir = Join-Path $ProjectRoot "Publish"
$OutputDir  = Join-Path $ProjectRoot "Output"

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host " CleanPanel - Automacao de Build e Instalador (.exe)" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan

# 1. Localizar ISCC.exe garantindo APENAS UMA string (Select-Object -First 1)
Write-Host "`n[1/5] Verificando instalacao do Inno Setup (ISCC.exe)..." -ForegroundColor Yellow

$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $isccPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    $cmdIscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmdIscc -and $cmdIscc.Source -and (Test-Path $cmdIscc.Source)) {
        $iscc = [string]$cmdIscc.Source
    }
}

if (-not $iscc) {
    Write-Host "`n[ERRO] O compilador do Inno Setup (ISCC.exe) nao foi encontrado no sistema!" -ForegroundColor Red
    Write-Host "Para instalar o Inno Setup 6 rapidamente via terminal, execute:" -ForegroundColor Yellow
    Write-Host "    winget install JRSoftware.InnoSetup" -ForegroundColor Green
    exit 1
}

$iscc = [string]$iscc
Write-Host "      ISCC.exe encontrado em: $iscc" -ForegroundColor Green

# Ler versao do CleanPanel.csproj (padrao 1.1.0)
$AppVersion = "1.1.0"
if (Test-Path $CsprojPath) {
    [xml]$CsprojXml = Get-Content $CsprojPath
    $InfoVersionNode = $CsprojXml.Project.PropertyGroup.InformationalVersion | Where-Object { $_ } | Select-Object -First 1
    $VersionNode = $CsprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ($InfoVersionNode) {
        $AppVersion = ([string]$InfoVersionNode).Trim()
    }
    elseif ($VersionNode) {
        $AppVersion = ([string]$VersionNode).Trim()
    }
}
Write-Host "      Versao detectada: $AppVersion" -ForegroundColor Gray

# 2. Validar cabecalho binario de Assets\app.ico (evita erro Win32 PE 0x800700C1)
Write-Host "`n[2/5] Validando cabecalho binario ICO em Assets\app.ico..." -ForegroundColor Yellow

$NeedRegenIco = $false
if (-not (Test-Path $IcoPath)) {
    $NeedRegenIco = $true
}
else {
    $HeaderBytes = [System.IO.File]::ReadAllBytes($IcoPath)
    if ($HeaderBytes.Length -lt 22 -or $HeaderBytes[0] -ne 0 -or $HeaderBytes[1] -ne 0 -or $HeaderBytes[2] -ne 1 -or $HeaderBytes[3] -ne 0) {
        $NeedRegenIco = $true
    }
}

if ($NeedRegenIco) {
    Write-Host "      Regerando Assets\app.ico com cabecalho ICONDIR/ICONDIRENTRY valido..." -ForegroundColor Yellow
    & dotnet build "$CsprojPath" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n[ERRO] Falha ao compilar projeto para regenerar o icone!" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    $BuiltDll = Join-Path $ProjectRoot "bin\Release\net8.0-windows\CleanPanel.dll"
    if (Test-Path $BuiltDll) {
        & dotnet exec "$BuiltDll" --generate-icon
    }
}

$VerifiedBytes = [System.IO.File]::ReadAllBytes($IcoPath)
$IcoCount = [BitConverter]::ToInt16($VerifiedBytes, 4)
$IcoOffset = [BitConverter]::ToInt32($VerifiedBytes, 18)
Write-Host "      Assets\app.ico validado (Tamanho: $($VerifiedBytes.Length) bytes | Count: $IcoCount | Offset: $IcoOffset)." -ForegroundColor Green

# 3. Limpar pastas temporarias de publicacao e saida anteriores
Write-Host "`n[3/5] Limpando pastas temporarias (Publish e Output)..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "      Diretorios limpos com sucesso." -ForegroundColor Green

# 4. Executar dotnet publish (Release, win-x64, Self-Contained=true, SingleFile=false)
Write-Host "`n[4/5] Publicando CleanPanel .NET 8 (win-x64, Self-Contained)..." -ForegroundColor Yellow

& dotnet publish "$CsprojPath" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o "$PublishDir"

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERRO] Falha durante a execucao do dotnet publish!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "      Publicacao concluida em: $PublishDir" -ForegroundColor Green

# 5. Invocar ISCC.exe apontando para installer.iss
Write-Host "`n[5/5] Compilando instalador CleanPanel_Setup.exe com Inno Setup..." -ForegroundColor Yellow

& "$iscc" "/DMyAppVersion=$AppVersion" "$IssPath"

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERRO] Falha ao compilar o instalador no Inno Setup!" -ForegroundColor Red
    exit $LASTEXITCODE
}

$InstallerFile = Join-Path $OutputDir "CleanPanel_Setup.exe"

if (Test-Path $InstallerFile) {
    $SizeMb = [math]::Round((Get-Item $InstallerFile).Length / 1MB, 2)
    Write-Host "`n====================================================================" -ForegroundColor Green
    Write-Host " INSTALADOR GERADO COM SUCESSO!" -ForegroundColor Green
    Write-Host " Arquivo : Output\CleanPanel_Setup.exe" -ForegroundColor White
    Write-Host " Caminho : $InstallerFile" -ForegroundColor Gray
    Write-Host " Tamanho : $SizeMb MB" -ForegroundColor Gray
    Write-Host "====================================================================" -ForegroundColor Green
}
else {
    Write-Host "`n[ERRO] O processo terminou, mas o arquivo $InstallerFile nao foi encontrado." -ForegroundColor Red
    exit 1
}
