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
