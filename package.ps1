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
param([string[]]$Arch = @("x64", "arm64"))
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$app = "isTargetSleeping"
$version = (Get-Content VERSION -Raw).Trim()
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

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1

foreach ($a in $Arch) {
    & "$PSScriptRoot\build.ps1" -Arch $a | Out-Host
    $exe = Join-Path $PSScriptRoot "build\$app.exe"
    Sign $exe

    $stage = Join-Path $dist "$app-$version-win-$a"
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $exe, LICENSE $stage -Force
    $zip = "$stage.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    Remove-Item $stage -Recurse -Force
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
