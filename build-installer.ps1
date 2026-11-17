<#
.SYNOPSIS
    Empacota e compila o instalador do Cleanpanel com versionamento dinâmico.

.DESCRIPTION
    Executa o pipeline completo de empacotamento:
    1. Valida e normaliza a versão informada via CLI para .NET e Inno Setup.
    2. Limpa artefatos de publicação e build anteriores.
    3. Publica o projeto .NET 8 em Release (win-x64, self-contained) injetando metadados de versão.
    4. Localiza o compilador ISCC.exe (Inno Setup 6/5) e gera o instalador final versionado.

.PARAMETER Versao
    Versão semântica ou numérica do instalador (ex.: 1.0, 1.1.0, v2.0.1).
    Aliases suportados: -Version, -v. Padrão: 1.0.0.

.EXAMPLE
    .\build-installer.ps1 -Versao 1.0

.EXAMPLE
    .\build-installer.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, HelpMessage = "Versão do instalador (ex.: 1.0, 1.1.0, v2.0.1).")]
    [Alias("Version", "v")]
    [string]$Versao = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-VersionMetadata {
    param([Parameter(Mandatory)][string]$RawVersion)

    $trimmed = $RawVersion.Trim() -replace '^[vV]', ''
    if ([string]::IsNullOrWhiteSpace($trimmed) -or
        $trimmed -notmatch '^(\d+(?:\.\d+){1,3})(?:-([0-9A-Za-z.-]+))?$') {
        throw "Formato de versão inválido: '$RawVersion'. Use formatos como 1.0, 1.1.0 ou 1.2.3.4."
    }

    $numericCore = $matches[1]
    $preReleaseSuffix = $matches[2]
    $segments = @($numericCore.Split('.') | ForEach-Object { [int]$_ })

    foreach ($segment in $segments) {
        if ($segment -lt 0 -or $segment -gt 65535) {
            throw "Segmento de versão fora do intervalo Win32 suportado (0..65535): $segment"
        }
    }

    $p0 = $segments[0]
    $p1 = if ($segments.Count -ge 2) { $segments[1] } else { 0 }
    $p2 = if ($segments.Count -ge 3) { $segments[2] } else { 0 }
    $p3 = if ($segments.Count -ge 4) { $segments[3] } else { 0 }

    $assemblyFourPart = "$p0.$p1.$p2.$p3"
    $dotNetCore = if ($segments.Count -eq 4) { $assemblyFourPart } else { "$p0.$p1.$p2" }
    $dotNetVersion = if ([string]::IsNullOrWhiteSpace($preReleaseSuffix)) { $dotNetCore } else { "$dotNetCore-$preReleaseSuffix" }

    return [PSCustomObject]@{
        CleanVersion        = $trimmed
        DotNetVersion       = $dotNetVersion
        AssemblyFileVersion = $assemblyFourPart
    }
}

function Find-InnoSetupCompiler {
    $pathCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($pathCmd) {
        return $pathCmd.Source
    }

    $regKeys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 5_is1"
    )

    foreach ($key in $regKeys) {
        if (Test-Path $key) {
            $installLocation = (Get-ItemProperty -Path $key -Name "InstallLocation" -ErrorAction SilentlyContinue).InstallLocation
            if ($installLocation) {
                $candidate = Join-Path $installLocation "ISCC.exe"
                if (Test-Path $candidate) {
                    return $candidate
                }
            }
        }
    }

    $commonPaths = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe",
        "$env:APPDATA\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
        "C:\Program Files\Inno Setup 5\ISCC.exe",
        "C:\ProgramData\chocolatey\bin\ISCC.exe",
        "$env:USERPROFILE\scoop\shims\ISCC.exe",
        "$env:USERPROFILE\scoop\apps\inno-setup\current\ISCC.exe"
    )

    return $commonPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

try {
    $ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $CsprojFile  = Join-Path $ProjectRoot "CleanPanel.csproj"
    $IssFile     = Join-Path $ProjectRoot "installer.iss"
    $PublishDir  = Join-Path $ProjectRoot "publish"
    $OutputDir   = Join-Path $ProjectRoot "installer\Output"

    if (-not (Test-Path $CsprojFile)) {
        throw "Arquivo de projeto não encontrado em: $CsprojFile"
    }

    if (-not (Test-Path $IssFile)) {
        throw "Script Inno Setup não encontrado em: $IssFile"
    }

    $verInfo = Resolve-VersionMetadata -RawVersion $Versao

    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host " Build e Empacotamento - Cleanpanel ($($verInfo.CleanVersion))" -ForegroundColor Cyan
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "  Versão instalador : $($verInfo.CleanVersion)"
    Write-Host "  Versão pacote .NET: $($verInfo.DotNetVersion)"
    Write-Host "  Versão assembly   : $($verInfo.AssemblyFileVersion)"
    Write-Host ""

    Write-Host "[1/4] Localizando compilador Inno Setup (ISCC.exe)..." -ForegroundColor Yellow
    $IsccExe = Find-InnoSetupCompiler
    if (-not $IsccExe) {
        throw "Compilador ISCC.exe não encontrado. Instale com: winget install JRSoftware.InnoSetup"
    }
    Write-Host "[OK] ISCC encontrado em: $IsccExe" -ForegroundColor Green

    Write-Host "[2/4] Limpando builds e publicações anteriores..." -ForegroundColor Yellow
    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
    }
    if (-not (Test-Path $OutputDir)) {
        New-Item -Path $OutputDir -ItemType Directory -Force | Out-Null
    }
    dotnet clean $CsprojFile -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar dotnet clean (código $LASTEXITCODE)."
    }

    Write-Host "[3/4] Publicando Cleanpanel (Release | win-x64 | self-contained)..." -ForegroundColor Yellow
    dotnet publish $CsprojFile `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $PublishDir `
        "-p:Version=$($verInfo.DotNetVersion)" `
        "-p:AssemblyVersion=$($verInfo.AssemblyFileVersion)" `
        "-p:FileVersion=$($verInfo.AssemblyFileVersion)" `
        "-p:InformationalVersion=$($verInfo.CleanVersion)"
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar dotnet publish (código $LASTEXITCODE)."
    }

    $publishedExe = Join-Path $PublishDir "Cleanpanel.exe"
    if (-not (Test-Path $publishedExe)) {
        throw "Executável esperado não encontrado após publish: $publishedExe"
    }

    Write-Host "[4/4] Compilando instalador com Inno Setup..." -ForegroundColor Yellow
    $isccArgs = @(
        "/DMyAppVersion=$($verInfo.CleanVersion)",
        "/DMyAppNumericVersion=$($verInfo.AssemblyFileVersion)",
        "/DMyBuildFolder=$PublishDir",
        "/DMyOutputDir=$OutputDir",
        "$IssFile"
    )
    & $IsccExe @isccArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Falha na compilação do Inno Setup (código $LASTEXITCODE)."
    }

    $expectedInstaller = Join-Path $OutputDir "Cleanpanel_Setup_$($verInfo.CleanVersion).exe"
    if (-not (Test-Path $expectedInstaller)) {
        throw "Instalador esperado não foi gerado em: $expectedInstaller"
    }

    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host "[SUCESSO] Instalador gerado: $expectedInstaller" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "[ERRO] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
