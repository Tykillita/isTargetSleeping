# Copia a site\media las imágenes, el ícono y los videos que usa la web, desde Assets y docs.
# Así la web no duplica archivos en el repo: media\ se genera (y lo ignora git).
# Uso: .\site\build.ps1            → deja site\ lista para abrir index.html o publicar
#      .\site\build.ps1 -Out _site → copia la web completa a otra carpeta (GitHub Pages)
param([string]$Out)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$site = $PSScriptRoot
$media = Join-Path $site 'media'
New-Item -ItemType Directory -Force $media | Out-Null

$files = @(
    'Assets\istargetsleeping-icon.svg',
    'Assets\istargetsleeping-icon-256.png',
    'Assets\istargetsleeping-icon-512.png',
    'Assets\istargetsleeping-mark-blue.svg',
    'Assets\istargetsleeping-mark-ink.svg',
    'Assets\istargetsleeping-mark-white.svg'
)
foreach ($lang in 'es', 'en') {
    $files += "docs\images\panel-$lang.png"
    $files += "docs\images\activity-$lang.png"
    $files += "docs\images\settings-preview-$lang.png"
    $files += "docs\images\idle-flow-$lang.svg"
    $files += "docs\images\game-mode-$lang.svg"
    $files += "docs\images\pets-home-$lang.svg"
    $files += "docs\images\pets-states-$lang.svg"
    $files += "docs\video\isTargetSleeping-tour-$lang.mp4"
}
foreach ($f in $files) {
    Copy-Item (Join-Path $root $f) $media -Force
}
Write-Host "media: $($files.Count) archivos en $media"

if ($Out) {
    $dest = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $site '*') $dest -Recurse -Force -Exclude 'build.ps1'
    Write-Host "web copiada a $dest"
}
