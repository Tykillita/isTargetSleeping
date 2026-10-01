# Regenera las imágenes del README y los assets del logo con la propia app.
# Usa datos de ejemplo: no toca Ollama ni tus ajustes. El acrílico de Windows se
# simula detrás del panel.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
& "$root\build.ps1" | Out-Null
$exe = "$root\build\isTargetSleeping.exe"
$img = "$PSScriptRoot\images"
New-Item -ItemType Directory -Force $img | Out-Null
Get-ChildItem $img -Filter *.png | Remove-Item

function Snap([string]$name, [string[]]$options) {
    & $exe --snapshot "$img\$name.png" @options | Write-Output
}
# La demo rellena la GPU, un llama-server, quién despierta al modelo y el historial.
Snap panel-en    @("demo", "--lang", "en")
Snap panel-es    @("demo", "--lang", "es")
Snap activity-en @("activity", "demo", "--lang", "en")
Snap activity-es @("activity", "demo", "--lang", "es")
Snap settings-en @("settings", "demo", "--lang", "en")
Snap settings-es @("settings", "demo", "--lang", "es")
& $exe --export-logo "$root\Assets" | Write-Output
# Las ilustraciones del README (mascotas animadas, infografías y pósters del video) salen
# de las hojas de --export-pet; hace falta Python con Pillow y numpy.
$python = Get-Command py, python -ErrorAction SilentlyContinue | Select-Object -First 1
if ($python) {
    $pets = Join-Path ([IO.Path]::GetTempPath()) "isTargetSleeping-pets"
    & $exe --export-pet $pets | Write-Output
    & $python.Source "$PSScriptRooteadme-art.py" $pets
    Remove-Item -Recurse -Force $pets
} else {
    Write-Warning "Python no está instalado: no se regeneran las ilustraciones del README"
}
