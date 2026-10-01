<#
.SYNOPSIS
  Empaqueta una versión de isTargetSleeping.

  Deja en dist\ para x64 y arm64:
    isTargetSleeping-<versión>-win-<arch>.zip   (portable: un solo .exe)
    isTargetSleeping-<versión>-setup-<arch>.exe (instalador por usuario, si Inno Setup 6 está instalado)
  cada uno con su .sha256.

  Firma (opcional): con SIGN_CERT_THUMBPRINT definido, firma el .exe y el
  instalador con signtool y sello de tiempo. Sin él sale sin firmar, solo para
  pruebas (SmartScreen avisará al abrirlo).

.EXAMPLE
  Set-Content VERSION 1.0.0
  $env:SIGN_CERT_THUMBPRINT = "…"; .\package.ps1
#>
param(
    [ValidateSet("x64", "arm64")] [string[]]$Arch = @("x64", "arm64"),
    [switch]$RequireInstaller,
    [string]$InnoCompiler
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$app = "isTargetSleeping"
$version = (Get-Content VERSION -Raw).Trim()
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw "VERSION debe tener una versión estable MAJOR.MINOR.PATCH." }
$dist = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

function Sign([string]$file) {
    if (-not $env:SIGN_CERT_THUMBPRINT) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "Falta signtool.exe (Windows SDK)." }
    & $signtool.FullName sign /sha1 $env:SIGN_CERT_THUMBPRINT /fd sha256 /tr http://timestamp.digicert.com /td sha256 $file
    if ($LASTEXITCODE -ne 0) { throw "signtool falló con $file" }
}

function Checksum([string]$file) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content "$file.sha256" "$hash  $(Split-Path $file -Leaf)" -NoNewline
}

$iscc = @($InnoCompiler, "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { $_ } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($RequireInstaller -and -not $iscc) { throw "Falta Inno Setup 6: la publicación requiere portable e instalador." }

foreach ($a in $Arch) {
    $buildOut = Join-Path $PSScriptRoot "obj\package-$a"
    & "$PSScriptRoot\build.ps1" -Arch $a -OutputDirectory $buildOut | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "La compilación $a falló." }
    $exe = Join-Path $buildOut "$app.exe"
    Sign $exe

    $stage = Join-Path $dist "$app-$version-win-$a"
    if (-not ([IO.Path]::GetFullPath($stage)).StartsWith($dist + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Ruta de paquete no válida." }
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $exe, LICENSE $stage -Force
    $zip = "$stage.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    Remove-Item -LiteralPath $stage -Recurse -Force
    Checksum $zip
    "zip: $zip"

    if ($iscc) {
        & $iscc /Qp "/DAppVersion=$version" "/DAppArch=$a" "/DSourceExe=$exe" "/O$dist" "packaging\$app.iss"
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup falló" }
        $setup = Join-Path $dist "$app-$version-setup-$a.exe"
        Sign $setup
        Checksum $setup
        "instalador: $setup"
    }
}
if (-not $iscc) { "Inno Setup 6 no está instalado: solo se generaron los .zip (winget install JRSoftware.InnoSetup)." }
