<# Valida la versión/notas y los ocho archivos antes de preparar una Release. #>
param([string]$Tag, [switch]$VerifyAssets, [string]$NotesFile, [string]$AssetsDirectory)
$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $releaseRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or ($Tag -and $Tag -cne "v$version")) {
    throw 'La etiqueta debe coincidir exactamente con VERSION (vMAJOR.MINOR.PATCH).'
}
$changelog = Get-Content (Join-Path $releaseRoot 'CHANGELOG.md') -Raw
$heading = '(?ms)^## \[' + [regex]::Escape($version) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## \[|\z)'
$section = [regex]::Match($changelog, $heading)
if (-not $section.Success -or -not $section.Groups['notes'].Value.Trim()) { throw 'Faltan las notas de esta versión en CHANGELOG.md.' }
if ($NotesFile) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($NotesFile), $section.Groups['notes'].Value.Trim(), [Text.UTF8Encoding]::new($false)) }
if ($VerifyAssets) {
    $assets = if ($AssetsDirectory) { [IO.Path]::GetFullPath($AssetsDirectory) } else { Join-Path $releaseRoot 'dist' }
    $expected = @()
    foreach ($arch in @('x64', 'arm64')) {
        foreach ($name in @("isTargetSleeping-$version-win-$arch.zip", "isTargetSleeping-$version-setup-$arch.exe")) {
            $file = Join-Path $assets $name
            $expected += $name, "$name.sha256"
            if (-not (Test-Path -LiteralPath $file) -or -not (Test-Path -LiteralPath "$file.sha256")) { throw "Falta $name o su SHA-256." }
            $checksum = (Get-Content -LiteralPath "$file.sha256" -Raw).Trim() -split '\s+'
            if ($checksum.Length -ne 2 -or $checksum[1] -cne $name -or $checksum[0] -notmatch '^[0-9a-fA-F]{64}$' -or
                (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $checksum[0]) { throw "SHA-256 incorrecto para $name." }
        }
    }
    $actual = @(Get-ChildItem -LiteralPath $assets -File | ForEach-Object Name)
    if ($actual.Count -ne 8 -or @(Compare-Object $expected $actual).Count -ne 0) { throw 'La publicación debe contener exactamente los ocho archivos esperados.' }
}
"Versión $version verificada."
