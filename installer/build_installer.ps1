param(
    [string]$Version = "1.1.0",
    [string]$BuildFolder = "..\publish"
)

$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$IssFile = Join-Path $ScriptPath "setup.iss"
$OutputDir = Join-Path $ScriptPath "Output"

$IsccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$IsccExe = $null
foreach ($path in $IsccPaths) {
    if (Test-Path $path) {
        $IsccExe = $path
        break
    }
}

if (-not $IsccExe) {
    Write-Host "[ERRO] ISCC.exe não encontrado. Instale o Inno Setup." -ForegroundColor Red
    exit 1
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Iniciando compilação do instalador v$Version" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# Executa o ISCC injetando os parâmetros dinâmicos
$process = Start-Process -FilePath $IsccExe -ArgumentList "/DMyAppVersion=`"$Version`" /DMyBuildFolder=`"$BuildFolder`" `"$IssFile`" /Q" -Wait -NoNewWindow -PassThru

if ($process.ExitCode -eq 0) {
    Write-Host "[SUCESSO] Instalador v$Version gerado em: $OutputDir" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Falha ao compilar instalador. Código: $($process.ExitCode)" -ForegroundColor Red
}
