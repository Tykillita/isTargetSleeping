param(
    [Parameter(Mandatory)][string]$OllamaPath,
    [Parameter(Mandatory)][ValidateRange(49152,65535)][int]$Port,
    [Parameter(Mandatory)][ValidatePattern('^isTargetSleeping-BackendVerification-Task-[a-f0-9]{8}$')][string]$TaskName,
    [ValidateSet('CurrentUserInteractive','System')][string]$PrincipalMode = 'CurrentUserInteractive',
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'obj/backend-verification/task')
)
$ErrorActionPreference = 'Stop'
$testRoot = [IO.Path]::GetFullPath($OutputDirectory)
$allowedOutputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path 'obj')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!(($testRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar).StartsWith($allowedOutputRoot,[StringComparison]::OrdinalIgnoreCase))) { throw 'OutputDirectory must be inside the workspace obj directory' }
$testOllamaPath = (Resolve-Path -LiteralPath $OllamaPath).Path
if ([IO.Path]::GetFileName($testOllamaPath) -ne 'ollama.exe') { throw 'Expected genuine ollama.exe' }
$taskPath = '\' + $TaskName
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
try { $existing = $folder.GetTask($taskPath) } catch { $existing = $null }
if ($null -ne $existing) { throw "Task already exists: $taskPath" }
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
try { $probe.Start() } finally { $probe.Stop() }
$runRoot = Join-Path $testRoot $TaskName
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $runRoot 'models') -Force | Out-Null
$wrapper = Join-Path $runRoot 'ollama-serve-task.ps1'
$escapedBinary = $testOllamaPath.Replace("'", "''")
$escapedRoot = $runRoot.Replace("'", "''")
$wrapperText = @'
$ErrorActionPreference = 'Stop'
$runRoot = '__ROOT__'
$env:OLLAMA_HOST = '127.0.0.1:__PORT__'
$env:OLLAMA_MODELS = Join-Path $runRoot 'models'
$env:OLLAMA_NO_CLOUD = '1'
$env:OLLAMA_NUM_PARALLEL = '1'
$env:OLLAMA_MAX_LOADED_MODELS = '1'
$child = Start-Process -FilePath '__BINARY__' -ArgumentList 'serve' -WorkingDirectory $runRoot -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runRoot 'server.stdout.log') -RedirectStandardError (Join-Path $runRoot 'server.stderr.log')
$shellPath = (Get-Process -Id $PID).Path
$wrapperProcess = [Diagnostics.Process]::GetCurrentProcess()
$evidence = [ordered]@{ Pid=$child.Id; CreatedUtcTicks=$child.StartTime.ToUniversalTime().Ticks; Executable='__BINARY__'; Port=__PORT__; StartedUtc=[DateTime]::UtcNow.ToString('o'); Wrapper=[ordered]@{ Pid=$PID; CreatedUtcTicks=$wrapperProcess.StartTime.ToUniversalTime().Ticks; Executable=$shellPath } }
$childTemporary = Join-Path $runRoot 'child.json.tmp'
[IO.File]::WriteAllText($childTemporary, ($evidence | ConvertTo-Json -Depth 6))
[IO.File]::Move($childTemporary,(Join-Path $runRoot 'child.json'),$true)
$child.WaitForExit()
exit $child.ExitCode
'@
$wrapperText = $wrapperText.Replace('__ROOT__',$escapedRoot).Replace('__BINARY__',$escapedBinary).Replace('__PORT__',[string]$Port)
[IO.File]::WriteAllText($wrapper,$wrapperText,[Text.UTF8Encoding]::new($false))
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
$definition = $service.NewTask(0)
$definition.RegistrationInfo.Description = 'Temporary isolated Ollama backend verification; no triggers; delete after test.'
if ($PrincipalMode -eq 'System') {
    $definition.Principal.UserId = 'S-1-5-18'
    $definition.Principal.LogonType = 5 # ServiceAccount, no interactive login or password
    $definition.Principal.RunLevel = 1 # Highest, for isolated CI runner only
} else {
    $definition.Principal.UserId = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $definition.Principal.LogonType = 3 # InteractiveToken
    $definition.Principal.RunLevel = 0 # Lowest
}
$definition.Settings.Enabled = $true
$definition.Settings.AllowDemandStart = $true
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.ExecutionTimeLimit = 'PT2M'
$definition.Settings.Hidden = $true
$definition.Settings.MultipleInstances = 2
$action = $definition.Actions.Create(0)
$action.Path = $pwsh
$action.Arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $wrapper + '"'
$action.WorkingDirectory = $runRoot
$manifest = [ordered]@{ TaskPath=$taskPath; TaskName=$TaskName; Port=$Port; Endpoint="http://127.0.0.1:$Port"; RunRoot=$runRoot; Wrapper=$wrapper; OllamaPath=$testOllamaPath; Principal=$definition.Principal.UserId; PrincipalMode=$PrincipalMode; LogonType=$definition.Principal.LogonType; RunLevel=$definition.Principal.RunLevel; Triggers=0; RegisteredUtc=[DateTime]::UtcNow.ToString('o') }
[IO.File]::WriteAllText((Join-Path $runRoot 'manifest.json'),($manifest | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$registered = $folder.RegisterTaskDefinition($TaskName,$definition,2,$definition.Principal.UserId,$null,$definition.Principal.LogonType,$null)
$manifest | ConvertTo-Json
