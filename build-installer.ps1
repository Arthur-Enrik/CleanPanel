<#
.SYNOPSIS
    Script automatizado de Build, Publish (.NET 8 Self-Contained) e Empacotamento do Instalador (.exe) do CleanPanel via Inno Setup.
.DESCRIPTION
    1. Localiza o compilador do Inno Setup (ISCC.exe).
    2. Limpa os diretorios temporarios de publicacao (./Publish) e saida (./Output).
    3. Executa o dotnet publish (Release, win-x64, self-contained=true, PublishSingleFile=false) para CleanPanel.csproj.
    4. Compila o script installer.iss gerando Output\CleanPanel_Setup.exe.
#>

$ErrorActionPreference = "Stop"

# Garante que o diretorio de trabalho seja a pasta onde o script esta localizado
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

$CsprojPath = Join-Path $ProjectRoot "CleanPanel.csproj"
$IssPath    = Join-Path $ProjectRoot "installer.iss"
$PublishDir = Join-Path $ProjectRoot "Publish"
$OutputDir  = Join-Path $ProjectRoot "Output"

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host " CleanPanel - Automacao de Build e Instalador (.exe)" -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan

# 1. Verificar se o Inno Setup (ISCC.exe) esta instalado
Write-Host "`n[1/4] Verificando instalacao do Inno Setup (ISCC.exe)..." -ForegroundColor Yellow

$IsccPath = $null
$CommandCheck = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue

if ($CommandCheck) {
    $IsccPath = $CommandCheck.Source
}
else {
    $CandidatePaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($Candidate in $CandidatePaths) {
        if (Test-Path $Candidate) {
            $IsccPath = $Candidate
            break
        }
    }
}

if (-not $IsccPath) {
    Write-Host "`n[ERRO] O compilador do Inno Setup (ISCC.exe) nao foi encontrado no sistema!" -ForegroundColor Red
    Write-Host "Para instalar o Inno Setup 6 rapidamente via terminal, execute:" -ForegroundColor Yellow
    Write-Host "    winget install JRSoftware.InnoSetup" -ForegroundColor Green
    exit 1
}

Write-Host "      ISCC.exe encontrado em: $IsccPath" -ForegroundColor Green

# Ler versao do CleanPanel.csproj (padrao 0.1v)
$AppVersion = "0.1v"
if (Test-Path $CsprojPath) {
    [xml]$CsprojXml = Get-Content $CsprojPath
    $InfoVersionNode = $CsprojXml.Project.PropertyGroup.InformationalVersion | Where-Object { $_ } | Select-Object -First 1
    $VersionNode = $CsprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ($InfoVersionNode) {
        $AppVersion = $InfoVersionNode.Trim()
    }
    elseif ($VersionNode) {
        $AppVersion = $VersionNode.Trim()
    }
}
Write-Host "      Versao detectada: $AppVersion" -ForegroundColor Gray

# 2. Limpar pastas temporarias de publicacao e saida anteriores
Write-Host "`n[2/4] Limpando pastas temporarias (Publish e Output)..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "      Diretorios limpos com sucesso." -ForegroundColor Green

# 3. Executar dotnet publish (Release, win-x64, Self-Contained=true, SingleFile=false)
Write-Host "`n[3/4] Publicando CleanPanel .NET 8 (win-x64, Self-Contained)..." -ForegroundColor Yellow

& dotnet publish $CsprojPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERRO] Falha durante a execucao do dotnet publish!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "      Publicacao concluida em: $PublishDir" -ForegroundColor Green

# 4. Invocar ISCC.exe apontando para installer.iss
Write-Host "`n[4/4] Compilando instalador CleanPanel_Setup.exe com Inno Setup..." -ForegroundColor Yellow

& $IsccPath "/DMyAppVersion=$AppVersion" $IssPath

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
