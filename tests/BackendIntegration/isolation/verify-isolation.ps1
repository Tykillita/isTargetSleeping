<#
.SYNOPSIS
  Comprueba que apagar un backend (tarea programada o binario) solo cierra SU servidor `ollama serve`
  y deja en marcha los demás (otra tarea, un servidor manual). Usa un «ollama.exe» falso: no toca el Ollama real.
.EXAMPLE
  .\tests\BackendIntegration\isolation\verify-isolation.ps1 -AppDll src\IsTargetSleeping\bin\Release\net10.0-windows10.0.19041.0\isTargetSleeping.dll
#>
param(
    [Parameter(Mandatory)][string]$AppDll,
    [string]$Dotnet = $(foreach ($c in @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe", "$env:ProgramFiles\dotnet\dotnet.exe")) { if ($c -and (Test-Path $c) -and ((& $c --list-sdks) -match '^1\d\.')) { $c; break } })
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('its-isolation-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = 0
$fakes = @{}
function Check([string]$name, [bool]$ok) { if ($ok) { "OK  $name" } else { "FAIL $name"; $script:failures++ } }
function Fake-Alive($p) { try { return !(Get-Process -Id $p.Id -ErrorAction Stop).HasExited } catch { return $false } }
function Wait-Dead($p, [int]$sec = 10) { $end = [DateTime]::UtcNow.AddSeconds($sec); while ([DateTime]::UtcNow -lt $end -and (Fake-Alive $p)) { Start-Sleep -Milliseconds 200 }; return !(Fake-Alive $p) }
function Fake-Server([string]$name) {
    $dir = Join-Path $root $name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Copy-Item (Join-Path $root 'bin\*') $dir -Recurse
    return Join-Path $dir 'ollama.exe'
}
try {
    & $Dotnet build (Join-Path $PSScriptRoot 'FakeOllama\FakeOllama.csproj') -c Release -o (Join-Path $root 'bin') --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'no compila FakeOllama' }
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AppDll).Path)
    $switch = $assembly.GetType('IsTargetSleeping.Switch', $true)
    $backendType = $assembly.GetType('IsTargetSleeping.Backend', $true)
    $kind = $assembly.GetType('IsTargetSleeping.BackendKind', $true)
    function New-Backend([string]$k, [string]$id, [string]$name) { [Activator]::CreateInstance($backendType, @([Enum]::Parse($kind, $k), $id, $name, $null)) }
    function Stop-Backend($b) { $switch.GetMethod('Stop', [Type[]]@($backendType)).Invoke($null, @($b)) }

    # --- tarea programada: A (tarea) + B (otra tarea) + M (manual) ---
    $exeA = Fake-Server 'taskA'; $exeB = Fake-Server 'taskB'; $exeM = Fake-Server 'manual'
    $names = @()
    foreach ($t in @(@('A', $exeA), @('B', $exeB))) {
        $name = 'isTargetSleeping-Isolation-' + $t[0] + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
        $names += $name
        $action = New-ScheduledTaskAction -Execute $t[1] -Argument 'serve'
        Register-ScheduledTask -TaskName $name -Action $action -Force | Out-Null
    }
    $path = @{ A = '\' + $names[0]; B = '\' + $names[1] }
    $running = @{}
    foreach ($k in 'A', 'B') {
        Start-ScheduledTask -TaskName $names[($k -eq 'A' ? 0 : 1)]
    }
    $manual = Start-Process $exeM -ArgumentList 'serve' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $procs = @{ A = Get-Process ollama | Where-Object { $_.Path -eq $exeA }; B = Get-Process ollama | Where-Object { $_.Path -eq $exeB } }
    Check 'A, B y el manual arrancan' ($procs.A -and $procs.B -and (Fake-Alive $manual))
    $backendA = New-Backend 'Task' $path.A $names[0]
    $message = Stop-Backend $backendA
    Check 'tarea: apagar A cierra su servidor' (Wait-Dead $procs.A)
    Check 'tarea: B (otra tarea) sigue en marcha' (Fake-Alive $procs.B)
    Check 'tarea: el servidor manual sigue en marcha' (Fake-Alive $manual)
    Check "tarea: avisa de apagado incompleto ($message)" ([bool]$message)
    $message = Stop-Backend $backendA
    Check 'tarea: con A ya detenida, B y el manual siguen' ((Fake-Alive $procs.B) -and (Fake-Alive $manual))
    Check 'tarea: con A ya detenida avisa en vez de cerrar' ([bool]$message)

    # --- binario: lo arrancó la app (se cierra) y el manual no ---
    $binary = New-Backend 'Binary' $exeA 'binary'
    $err = $switch.GetMethod('Start', [Type[]]@($backendType)).Invoke($null, @($binary))
    Start-Sleep -Seconds 2
    $started = Get-Process ollama | Where-Object { $_.Path -eq $exeA }
    Check 'binario: la app lo arranca' ($null -eq $err -and $started)
    $message = Stop-Backend $binary
    Check 'binario: apagar cierra el que arrancó la app' ($started -and (Wait-Dead $started))
    Check 'binario: B y el manual siguen en marcha' ((Fake-Alive $procs.B) -and (Fake-Alive $manual))
    $message = Stop-Backend $binary
    Check 'binario: sin PID propio no cierra ninguno y avisa' ((Fake-Alive $manual) -and [bool]$message)
}
finally {
    foreach ($n in $names) { Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue }
    Get-Process ollama -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures -gt 0) { "$failures fallos"; exit 1 }
'todo bien'
