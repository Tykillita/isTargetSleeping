param(
    [Parameter(Mandatory)][string]$OllamaPath,
    [Parameter(Mandatory)][ValidateRange(49152,65535)][int]$Port,
    [string]$AppDll,
    [string]$AppExe,
    [ValidateRange(1,3)][int]$Cycles = 2,
    [string]$ExpectedVersion = (Get-Content -LiteralPath 'VERSION' -Raw).Trim(),
    [string]$ExpectedOllamaVersion = '0.35.1',
    [ValidateSet('CurrentUserInteractive','System')][string]$PrincipalMode = 'CurrentUserInteractive',
    [switch]$UseFullSwitch,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'obj/backend-verification/task')
)
$ErrorActionPreference = 'Stop'
$taskName = 'isTargetSleeping-BackendVerification-Task-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$allowedOutputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path 'obj')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!(($OutputDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar).StartsWith($allowedOutputRoot,[StringComparison]::OrdinalIgnoreCase))) { throw 'OutputDirectory must be inside the workspace obj directory' }
$runRoot = Join-Path $OutputDirectory $taskName
$manifestPath = Join-Path $runRoot 'manifest.json'
$events = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ Kind='Task'; TaskName=$taskName; Endpoint="http://127.0.0.1:$Port"; StartedUtc=[DateTime]::UtcNow.ToString('o'); ExpectedVersion=$ExpectedVersion; ExpectedOllamaVersion=$ExpectedOllamaVersion; CliCalls=@(); Cycles=@(); Passed=$false; Cleanup=$null }
function Assert-Probe([bool]$Condition,[string]$Description) {
    if (!$Condition) { throw $Description }
    $events.Add([ordered]@{ Utc=[DateTime]::UtcNow.ToString('o'); Assert=$Description; Passed=$true })
}
function Wait-Probe([scriptblock]$Predicate,[int]$Seconds=25) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { if (& $Predicate) { return $true }; Start-Sleep -Milliseconds 250 } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}
function Api-Version {
    try { return Invoke-RestMethod -Uri "$($report.Endpoint)/api/version" -TimeoutSec 1 -ErrorAction Stop } catch { return $null }
}
function Owned-Child-Alive($Child) {
    try { $process = Get-Process -Id $Child.Pid -ErrorAction Stop } catch { return $false }
    try {
        if ($process.StartTime.ToUniversalTime().Ticks -ne $Child.CreatedUtcTicks -or $process.Path -ne $Child.Executable) { throw 'Child identity changed; PID reused' }
        return !$process.HasExited
    } finally { $process.Dispose() }
}
function User-Ollama-Snapshot {
    $processIdentities = @(
        foreach ($process in (Get-Process -Name 'ollama*' -ErrorAction SilentlyContinue)) {
            try { [ordered]@{ Pid=$process.Id; CreatedUtcTicks=$process.StartTime.ToUniversalTime().Ticks; Name=$process.ProcessName; Executable=$process.Path } }
            finally { $process.Dispose() }
        }
    )
    $processIdentities = @($processIdentities | Sort-Object Pid)
    $listeners = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq 11434 } | ForEach-Object { $_.ToString() } | Sort-Object)
    return [ordered]@{ Processes=@($processIdentities); Listeners11434=$listeners }
}
function Assert-No-Foreign-Ollama($ExpectedChild = $null) {
    foreach ($process in (Get-Process -Name 'ollama*' -ErrorAction SilentlyContinue)) {
        try {
            if ($null -eq $ExpectedChild -or $process.Id -ne $ExpectedChild.Pid -or $process.StartTime.ToUniversalTime().Ticks -ne $ExpectedChild.CreatedUtcTicks -or $process.Path -ne $ExpectedChild.Executable) {
                throw "Foreign Ollama process found ($($process.Id)); refusing full Switch action"
            }
        } finally { $process.Dispose() }
    }
}
function Fixture-Exited($Child) {
    return !(Owned-Child-Alive $Child) -and !(Owned-Child-Alive $Child.Wrapper) -and !(Owned-Child-Alive $Child.Auxiliary)
}
function Invoke-Cli([string]$Flag) {
    $start = [Diagnostics.ProcessStartInfo]::new($report.AppExe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($Flag)
    $start.ArgumentList.Add('--lang')
    $start.ArgumentList.Add('en')
    $start.Environment['OLLAMA_HOST'] = "127.0.0.1:$Port"
    $start.Environment['OLLAMA_MODELS'] = Join-Path $runRoot 'models'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (!$process.Start()) { throw "Could not launch CLI $Flag" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(115000)) { $process.Kill($true); $process.WaitForExit(); throw "CLI timed out: $Flag" }
        $watch.Stop()
        return [ordered]@{ Flag=$Flag; ExitCode=$process.ExitCode; Stdout=$stdout.GetAwaiter().GetResult(); Stderr=$stderr.GetAwaiter().GetResult(); DurationMs=$watch.ElapsedMilliseconds }
    } finally { $process.Dispose() }
}
function Task-IsRunning {
    if ($null -ne $type) { return $type.GetMethod('IsRunning').Invoke($null,[object[]]@($manifest.TaskPath)) }
    try { return [int]$taskFolder.GetTask($manifest.TaskPath).State -eq 4 } catch { return $false }
}
function Invoke-ProductionAction([string]$Verb,$ExpectedChild = $null) {
    if ($report.AppExe) {
        Assert-No-Foreign-Ollama $ExpectedChild
        $flag = $(if ($Verb -eq 'Start') { '--on' } else { '--off' })
        $result = Invoke-Cli $flag
        $report.CliCalls += $result
        if ($result.ExitCode -ne 0) { return "CLI $flag exit $($result.ExitCode): $($result.Stdout) $($result.Stderr)" }
        Assert-Probe ($result.Stdout.Contains('(' + $taskName + ')')) "CLI $flag selected isolated Task backend"
        return $null
    }
    if ($UseFullSwitch) {
        Assert-No-Foreign-Ollama $ExpectedChild
        return $switchType.GetMethod($Verb).Invoke($null,[object[]]@($backend))
    }
    return $type.GetMethod($Verb).Invoke($null,[object[]]@($manifest.TaskPath))
}
function Verify-Cli-Status {
    $status = Invoke-Cli '--status'
    $report.CliCalls += $status
    # Backend.Summary returns Task.Name; Task.Path carries the leading backslash.
    Assert-Probe ($status.ExitCode -eq 0 -and $status.Stdout -match ('(?m)^mecanismo:\s+' + [regex]::Escape($taskName) + '\s+·')) 'CLI --status detects isolated Task backend'
}
try {
    $report.Baseline = User-Ollama-Snapshot
    if ($AppExe) {
        $report.AppExe = (Resolve-Path -LiteralPath $AppExe).Path
        $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($report.AppExe)
        $report.AppVersion = $fileVersion.FileVersion
        Assert-Probe (([Version]$fileVersion.FileVersion).ToString(3) -eq $ExpectedVersion) "Published app EXE version $ExpectedVersion"
        Assert-No-Foreign-Ollama
        $type = $null
        $taskService = New-Object -ComObject Schedule.Service
        $taskService.Connect()
        $taskFolder = $taskService.GetFolder('\')
    } else {
        if (!$AppDll) { throw 'Specify AppExe or AppDll' }
        $report.AppDll = (Resolve-Path -LiteralPath $AppDll).Path
        $assembly = [Reflection.Assembly]::LoadFrom($report.AppDll)
        $report.AppVersion = $assembly.GetName().Version.ToString()
        Assert-Probe ($assembly.GetName().Version.ToString(3) -eq $ExpectedVersion) "Loaded production app DLL $ExpectedVersion"
        $type = $assembly.GetType('IsTargetSleeping.ScheduledTasks',$true)
        $switchType = $assembly.GetType('IsTargetSleeping.Switch',$true)
        if ($UseFullSwitch) { Assert-No-Foreign-Ollama }
    }
    $registerJson = & (Join-Path $PSScriptRoot 'register-task.ps1') -OllamaPath $OllamaPath -Port $Port -TaskName $taskName -PrincipalMode $PrincipalMode -OutputDirectory $OutputDirectory
    $manifest = $registerJson | ConvertFrom-Json
    $report.Principal = $manifest.Principal
    $report.PrincipalMode = $manifest.PrincipalMode
    $report.LogonType = $manifest.LogonType
    $report.RunLevel = $manifest.RunLevel
    $report.ProductionPath = $(if ($report.AppExe) { 'Published CLI --status/--on/--off -> Detector -> Switch -> ScheduledTasks' } elseif ($UseFullSwitch) { 'Switch.Start/Stop(Task)' } else { 'ScheduledTasks.Start/Stop' })
    if ($report.AppExe) {
        Verify-Cli-Status
        $report.Backend = [ordered]@{ Id=$manifest.TaskPath; Name=$taskName; Kind='Task' }
    } else {
        $backends = $type.GetMethod('Ollama').Invoke($null,@())
        $ownBackend = @($backends | Where-Object { $_.Id -eq $manifest.TaskPath })
        Assert-Probe ($ownBackend.Count -eq 1 -and $ownBackend[0].Kind.ToString() -eq 'Task') 'Production ScheduledTasks.Ollama identifies isolated fixture'
        $report.Backend = [ordered]@{ Id=$ownBackend[0].Id; Name=$ownBackend[0].Name; Kind=$ownBackend[0].Kind.ToString() }
        $backend = $ownBackend[0]
    }
    Assert-Probe (!(Task-IsRunning)) 'Task initially stopped'
    for ($cycle=1; $cycle -le $Cycles; $cycle++) {
        $childMetadataPath = Join-Path $runRoot 'child.json'
        if (Test-Path -LiteralPath $childMetadataPath) { Remove-Item -LiteralPath $childMetadataPath }
        $cycleReport = [ordered]@{ Cycle=$cycle; StartError=$null; ApiVersion=$null; TagsCount=$null; Child=$null; StartMs=0; StopError=$null; StopMs=0; TaskStopped=$false; ChildExited=$false; DescendantsExited=$false; ApiClosed=$false }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $cycleReport.StartError = Invoke-ProductionAction 'Start'
        Assert-Probe ($null -eq $cycleReport.StartError) "Cycle $cycle production Start returns success"
        $started = Wait-Probe { $null -ne (Api-Version) -and (Task-IsRunning) -and (Test-Path -LiteralPath $childMetadataPath) }
        $watch.Stop()
        $cycleReport.StartMs = $watch.ElapsedMilliseconds
        Assert-Probe $started "Cycle $cycle API online and Task state Running"
        if ($report.AppExe) { Verify-Cli-Status }
        $cycleReport.ApiVersion = (Api-Version).version
        Assert-Probe ($cycleReport.ApiVersion -eq $ExpectedOllamaVersion) "Cycle $cycle API confirms genuine Ollama $ExpectedOllamaVersion"
        $tags = Invoke-RestMethod -Uri "$($report.Endpoint)/api/tags" -TimeoutSec 2
        $cycleReport.TagsCount = @($tags.models).Count
        Assert-Probe ($cycleReport.TagsCount -eq 0) "Cycle $cycle isolated model directory has no models"
        $cycleReport.Child = Get-Content -LiteralPath $childMetadataPath -Raw | ConvertFrom-Json
        Assert-Probe (Owned-Child-Alive $cycleReport.Child) "Cycle $cycle genuine Ollama process alive with expected PID and creation time"
        Assert-Probe ((Owned-Child-Alive $cycleReport.Child.Wrapper) -and (Owned-Child-Alive $cycleReport.Child.Auxiliary)) "Cycle $cycle task wrapper and auxiliary descendant alive"
        if ($report.Cycles.Count -gt 0) {
            $previousChild = $report.Cycles[-1].Child
            foreach ($pair in @(@($cycleReport.Child,$previousChild),@($cycleReport.Child.Wrapper,$previousChild.Wrapper),@($cycleReport.Child.Auxiliary,$previousChild.Auxiliary))) {
                Assert-Probe (($pair[0].Pid -ne $pair[1].Pid) -or ($pair[0].CreatedUtcTicks -ne $pair[1].CreatedUtcTicks)) "Cycle $cycle started a fresh process identity"
            }
        }
        Assert-Probe ($cycleReport.Child.Port -eq $Port) "Cycle $cycle fixture bound to private port"
        $secondStart = Invoke-ProductionAction 'Start' $cycleReport.Child
        Assert-Probe ($null -eq $secondStart) "Cycle $cycle repeated production Start does not error"
        $watch.Restart()
        $cycleReport.StopError = Invoke-ProductionAction 'Stop' $cycleReport.Child
        Assert-Probe ($null -eq $cycleReport.StopError) "Cycle $cycle production Stop returns success"
        $stopped = Wait-Probe { !(Task-IsRunning) -and (Fixture-Exited $cycleReport.Child) -and $null -eq (Api-Version) }
        $watch.Stop()
        $cycleReport.StopMs = $watch.ElapsedMilliseconds
        $cycleReport.TaskStopped = !(Task-IsRunning)
        $cycleReport.ChildExited = !(Owned-Child-Alive $cycleReport.Child)
        $cycleReport.DescendantsExited = Fixture-Exited $cycleReport.Child
        $cycleReport.ApiClosed = $null -eq (Api-Version)
        $report.Cycles += $cycleReport
        Assert-Probe $stopped "Cycle $cycle task stopped, actual Ollama PID exited, API offline"
        $secondStop = Invoke-ProductionAction 'Stop'
        Assert-Probe ($null -eq $secondStop) "Cycle $cycle repeated production Stop does not error"
    }
    $report.Passed = $true
} catch {
    $report.Failure = $_.Exception.ToString()
} finally {
    if (Test-Path -LiteralPath $manifestPath) {
        try { $report.Cleanup = (& (Join-Path $PSScriptRoot 'cleanup-task.ps1') -ManifestPath $manifestPath -OutputDirectory $OutputDirectory) | ConvertFrom-Json }
        catch { $report.CleanupError = $_.Exception.ToString(); $report.Passed=$false }
    }
    try {
        $report.After = User-Ollama-Snapshot
        $report.UserOllamaUnchanged = (($report.Baseline | ConvertTo-Json -Depth 10 -Compress) -eq ($report.After | ConvertTo-Json -Depth 10 -Compress))
        if (!$report.UserOllamaUnchanged) { $report.Passed=$false; $report.UserOllamaWarning='Original Ollama process identities or normal port listener changed during verification.' }
    } catch { $report.BaselineVerificationError=$_.Exception.ToString(); $report.Passed=$false }
    $report.Events = $events.ToArray()
    $report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $json = $report | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText((Join-Path $runRoot 'report.json'),$json,[Text.UTF8Encoding]::new($false))
    $json
}
if (!$report.Passed) { exit 1 }
