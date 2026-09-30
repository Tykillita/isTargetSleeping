<#
.SYNOPSIS
  Compila isTargetSleeping.

.EXAMPLE
  .\build.ps1              # build\isTargetSleeping.exe: un solo .exe, sin instalar .NET
  .\build.ps1 -Install     # además lo copia a %LOCALAPPDATA%\Programs\isTargetSleeping y lo relanza
  .\build.ps1 -Test        # pruebas de la lógica pura (IdleTracker, vigilante, modo juego, actualizador…)
  .\build.ps1 -Arch arm64  # para Windows en ARM
#>
param(
    [switch]$Install,
    [switch]$Test,
    [ValidateSet("x64", "arm64")] [string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" })
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$app = "isTargetSleeping"

# El SDK de .NET 10: el del sistema o el instalado por usuario con dotnet-install.ps1.
function Find-Dotnet {
    foreach ($candidate in @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe", "$env:ProgramFiles\dotnet\dotnet.exe")) {
        if ($candidate -and (Test-Path $candidate) -and ((& $candidate --list-sdks) -match '^1\d\.')) { return $candidate }
    }
    throw "Falta el SDK de .NET 10. Instálalo con: winget install Microsoft.DotNet.SDK.10"
}
$dotnet = Find-Dotnet
$env:DOTNET_ROOT = Split-Path $dotnet

if ($Test) {
    & $dotnet run --project tests\IsTargetSleeping.Tests -c Release
    exit $LASTEXITCODE
}

$out = Join-Path $PSScriptRoot "build"
# Primero el agente de memoria (lo único que corre como administrador); la app lo lleva dentro.
$agentOut = Join-Path $PSScriptRoot "obj\agent-$Arch"
& $dotnet publish src\IsTargetSleeping.Agent\IsTargetSleeping.Agent.csproj -c Release -r "win-$Arch" -o $agentOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$agent = Join-Path $agentOut "isTargetSleeping.MemoryAgent.exe"
& $dotnet publish src\IsTargetSleeping\IsTargetSleeping.csproj -c Release -r "win-$Arch" --self-contained true -o $out -p:PublishSingleFile=true "-p:AgentExe=$agent"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem $out -Exclude "$app.exe" | Remove-Item -Recurse -Force
$exe = Join-Path $out "$app.exe"
"{0} ({1:0.0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA "Programs\$app"
    Get-Process $app -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $exe $dest -Force
    $link = Join-Path ([Environment]::GetFolderPath("Programs")) "$app.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = Join-Path $dest "$app.exe"
    $shortcut.Save()
    Start-Process (Join-Path $dest "$app.exe")
    "instalado en $dest (y en el menú Inicio)"
}
