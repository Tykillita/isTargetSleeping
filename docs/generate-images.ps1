# Capturas nativas a 4×, sprites y recursos del README. Solo usa datos de ejemplo.
param([string]$Dotnet, [string]$Python)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (-not $Dotnet) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
}
& $Dotnet run --project "$PSScriptRoot\MediaCapture" -c Release -- $root --views-only
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron renderizar las vistas nativas.' }
$app = Join-Path $root 'src\IsTargetSleeping\bin\Release\net10.0-windows10.0.19041.0\isTargetSleeping.dll'
& $Dotnet $app --export-logo "$root\Assets"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo exportar el logo.' }
$pets = Join-Path $root 'obj\tour-work\pets'
& $Dotnet $app --export-pet $pets
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron exportar las mascotas.' }
if (-not $Python) {
    $Python = Get-Command python, py -All -ErrorAction SilentlyContinue | ForEach-Object Source |
        Where-Object { & $_ -c 'import PIL, numpy' 2>$null; $LASTEXITCODE -eq 0 } | Select-Object -First 1
}
if ($Python) {
    & $Python "$PSScriptRoot\readme-art.py" $pets
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron generar las ilustraciones del README.' }
} else {
    Write-Warning 'Falta Python con Pillow y numpy: no se regeneran las ilustraciones del README.'
}
