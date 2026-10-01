<# Prepara el compilador oficial dentro de obj, sin depender del runner. #>
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'obj/tooling'
$compilerRoot = Join-Path $toolRoot 'inno'
$compiler = Join-Path $compilerRoot 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
    $installer = Join-Path $toolRoot 'innosetup-6.7.3.exe'
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $installer
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ine '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732') {
        throw 'El compilador de Inno Setup no coincide con su SHA-256 esperado.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') {
        throw 'La firma del compilador de Inno Setup no es válida.'
    }
    $install = Start-Process -FilePath $installer -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', ('/DIR="' + $compilerRoot + '"'))
    if ($install.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'No se pudo preparar Inno Setup 6.' }
}
$compiler
