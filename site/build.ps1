# Copia los medios desde Assets y docs sin duplicarlos en el repositorio.
# Sin -Out: actualiza site/media para la vista previa local.
# Con -Out _site: genera la web de producción y versiona sus recursos por contenido.
param([string]$Out)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$site = $PSScriptRoot
$media = Join-Path $site 'media'
$publicUrl = 'https://istargetsleeping.web.app/'

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
    $files += "docs\images\tour-$lang.jpg"
    $files += "docs\video\isTargetSleeping-tour-$lang.mp4"
}

# Comprueba todas las fuentes antes de sustituir una copia de la vista previa.
foreach ($file in $files) {
    $source = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Falta un recurso de la web: $file"
    }
}
New-Item -ItemType Directory -Force $media | Out-Null
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $media -Force
}

$hashes = @{}
foreach ($file in $files) {
    $name = [IO.Path]::GetFileName($file)
    $hashes["media/$name"] = (Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
}
foreach ($file in 'site.css', 'site.js', 'firebase.js') {
    $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $site $file) -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
}

function Get-LocalPath([string]$Url) {
    if ($Url -match '^(?:[a-z][a-z0-9+.-]*:|//|#)' -or -not $Url) { return $null }
    $path = ($Url -split '[?#]', 2)[0]
    if ($path.StartsWith('/') -or $path.Contains('..') -or $path.Contains('\')) {
        throw "Ruta local no válida en la web: $Url"
    }
    return $path
}

function Get-VersionedUrl([string]$Url) {
    $path = Get-LocalPath $Url
    if (-not $path) { return $Url }
    if (-not (Test-Path -LiteralPath (Join-Path $site $path) -PathType Leaf)) {
        throw "Referencia local sin archivo: $Url"
    }
    if (-not $hashes.ContainsKey($path)) { return $Url }
    $fragment = if ($Url.Contains('#')) { '#' + ($Url -split '#', 2)[1] } else { '' }
    return "$path`?v=$($hashes[$path])$fragment"
}

$pages = @{}
foreach ($name in 'index.html', 'novedades.html') {
    $pages[$name] = [IO.File]::ReadAllText((Join-Path $site $name))
}

# Un mismo hash para ES/EN permite mantener {lang} al cambiar de idioma.
# Cualquier cambio en uno de los dos archivos invalida ambas URLs de la pareja.
$templatePattern = '\bdata-(?:src|poster)-lang\s*=\s*(?<quote>["''])(?<url>[^"'']+)\k<quote>'
foreach ($page in $pages.Values) {
    foreach ($match in [regex]::Matches($page, $templatePattern)) {
        $template = ($match.Groups['url'].Value -split '[?#]', 2)[0]
        if ($template -notmatch '\{lang\}') { throw "Plantilla sin {lang}: $template" }
        $pair = foreach ($lang in 'es', 'en') {
            $path = Get-LocalPath ($template.Replace('{lang}', $lang))
            if (-not $path -or -not $hashes.ContainsKey($path)) {
                throw "Falta un idioma o recurso en la plantilla: $template ($lang)"
            }
            $path
        }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($pair | ForEach-Object {
            (Get-FileHash -LiteralPath (Join-Path $site $_) -Algorithm SHA256).Hash
        }) -join ':')
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $digest = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant().Substring(0, 16)
        } finally {
            $sha.Dispose()
        }
        foreach ($path in $pair) { $hashes[$path] = $digest }
        $hashes[$template] = $digest
    }
}

$attributePattern = '(?<prefix>\b(?:src|href|poster|data-src-lang|data-poster-lang)\s*=\s*)(?<quote>["''])(?<url>[^"'']+)\k<quote>'
foreach ($name in @($pages.Keys)) {
    $pages[$name] = [regex]::Replace($pages[$name], $attributePattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $url = $match.Groups['url'].Value
        if ($match.Groups['prefix'].Value -match '^data-(?:src|poster)-lang') {
            $template = ($url -split '[?#]', 2)[0]
            $versioned = "$template`?v=$($hashes[$template])"
        } else {
            $versioned = Get-VersionedUrl $url
        }
        return $match.Groups['prefix'].Value + $match.Groups['quote'].Value + $versioned + $match.Groups['quote'].Value
    })
    $pages[$name] = [regex]::Replace($pages[$name], '<meta\b[^>]*\bproperty=["'']og:image["''][^>]*>', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return [regex]::Replace($match.Value, '(?<prefix>\bcontent\s*=\s*)(?<quote>["''])(?<url>[^"'']+)\k<quote>', [Text.RegularExpressions.MatchEvaluator]{
            param($content)
            $url = $content.Groups['url'].Value
            $path = Get-LocalPath $url
            $versioned = if ($path) { $publicUrl + (Get-VersionedUrl $url) } else { $url }
            return $content.Groups['prefix'].Value + $content.Groups['quote'].Value + $versioned + $content.Groups['quote'].Value
        })
    })
}
Write-Host "media: $($files.Count) archivos; referencias locales e idiomas validados"

if ($Out) {
    $dest = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }))
    $workspacePrefix = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $dest.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $dest.Equals([IO.Path]::GetFullPath($site), [StringComparison]::OrdinalIgnoreCase) -or
        $dest.StartsWith([IO.Path]::GetFullPath($site) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFullPath($site).StartsWith($dest + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'La salida debe ser otra carpeta dentro del workspace, fuera de site.'
    }
    New-Item -ItemType Directory -Force $dest | Out-Null
    foreach ($file in 'site.css', 'site.js', 'firebase.js') {
        Copy-Item -LiteralPath (Join-Path $site $file) -Destination $dest -Force
    }
    $outputMedia = Join-Path $dest 'media'
    New-Item -ItemType Directory -Force $outputMedia | Out-Null
    foreach ($file in $files) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $outputMedia -Force
    }
    foreach ($name in $pages.Keys) {
        [IO.File]::WriteAllText((Join-Path $dest $name), $pages[$name], [Text.UTF8Encoding]::new($false))
    }
    Write-Host "web de producción: $dest (recursos con hash de contenido)"
}
