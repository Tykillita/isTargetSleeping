param(
    [Parameter(Mandatory)][string]$AppExe,
    [Parameter(Mandatory)][string]$OllamaPath,
    [Parameter(Mandatory)][string]$FixtureExe,
    [ValidateRange(49152,65535)][int]$Port = 59154,
    [ValidateRange(1,3)][int]$Cycles = 2,
    [string]$ExpectedVersion = '',
    [string]$ExpectedOllamaVersion = '0.35.1'
)
$ErrorActionPreference = 'Stop'
$hostExecutable = (Resolve-Path -LiteralPath $FixtureExe).Path
$runtimeDirectory = Split-Path -Parent $hostExecutable
$fixtureDirectory = [IO.Path]::GetFullPath((Join-Path $runtimeDirectory '..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ($fixtureDirectory -notmatch '[\\/](backend-verification|backend-integration)[\\/]service$') { throw 'Fixture output must stay under backend-verification/service or backend-integration/service' }
$sourceOllama = (Resolve-Path -LiteralPath $OllamaPath).Path
$appExecutable = (Resolve-Path -LiteralPath $AppExe).Path
if (!$ExpectedVersion) { $ExpectedVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../../VERSION') -Raw).Trim() }
$expectedFileVersion = ([Version]$ExpectedVersion).ToString(3) + '.0'
$copiedOllama = Join-Path $runtimeDirectory 'ollama.exe'
$runId = [Guid]::NewGuid().ToString('N').Substring(0,12)
$serviceName = 'isTargetSleeping-BackendVerification-Service-' + $runId
$displayName = 'isTargetSleeping Ollama SCM verification ' + $runId
$runDirectory = Join-Path $fixtureDirectory "data/$runId"
$configPath = Join-Path $fixtureDirectory "config-$runId.json"
$reportPath = Join-Path $fixtureDirectory "service-$runId.report.json"
$eventPath = Join-Path $runDirectory 'logs/host-events.jsonl'
$endpoint = "http://127.0.0.1:$Port"
$createdService = $false
$checks = [Collections.Generic.List[object]]::new()
$cliResults = [Collections.Generic.List[object]]::new()
$aclBackups = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    Kind='Service'; ServiceName=$serviceName; DisplayName=$displayName
    StartedUtc=[DateTime]::UtcNow.ToString('O'); Passed=$false; Endpoint=$endpoint
    Account='NT AUTHORITY\LocalService'; FixtureRoot=$fixtureDirectory
    AppExe=$appExecutable; AppVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($appExecutable).FileVersion
    ExpectedAppVersion=$expectedFileVersion; ExpectedOllamaVersion=$ExpectedOllamaVersion
    AppSha256=(Get-FileHash -LiteralPath $appExecutable -Algorithm SHA256).Hash
    OllamaSource=$sourceOllama; OllamaSourceSha256=(Get-FileHash -LiteralPath $sourceOllama -Algorithm SHA256).Hash
    Windows=$null; Checks=$checks; Cli=$cliResults; Cycles=@(); Cleanup=$null; Error=$null
}

function Assert-Probe([bool]$Condition,[string]$Description) {
    $checks.Add([ordered]@{ Utc=[DateTime]::UtcNow.ToString('O'); Assert=$Description; Passed=$Condition })
    if (!$Condition) { throw $Description }
    Write-Host "PASS: $Description"
}
function Wait-Probe([scriptblock]$Predicate,[int]$Seconds=30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { if (& $Predicate) { return $true }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}
function Invoke-Sc([string[]]$Arguments,[switch]$AllowFailure) {
    $info = [Diagnostics.ProcessStartInfo]::new('sc.exe')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($item in $Arguments) { $info.ArgumentList.Add($item) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'sc.exe exceeded 15 seconds' }
        $result = [ordered]@{ Arguments=$Arguments; ExitCode=$process.ExitCode; Stdout=$outTask.GetAwaiter().GetResult(); Stderr=$errTask.GetAwaiter().GetResult() }
        if (!$AllowFailure -and $result.ExitCode -ne 0) { throw "sc.exe failed ($($result.ExitCode)): $($result.Stdout) $($result.Stderr)" }
        return $result
    } finally { $process.Dispose() }
}
function Get-FixtureService {
    Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
}
function Service-IsStopped {
    $service = Get-FixtureService
    return $null -eq $service -or $service.State -eq 'Stopped'
}
function Api-Version {
    try { Invoke-RestMethod -Uri "$endpoint/api/version" -TimeoutSec 1 -ErrorAction Stop } catch { return $null }
}
function User-Snapshot {
    $identities = @(
        foreach ($process in (Get-Process -Name 'ollama*','isTargetSleeping' -ErrorAction SilentlyContinue)) {
            try { [ordered]@{ Pid=$process.Id; CreatedUtcTicks=$process.StartTime.ToUniversalTime().Ticks; Name=$process.ProcessName; Executable=$process.Path } }
            finally { $process.Dispose() }
        }
    )
    $listeners = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq 11434 } | ForEach-Object { $_.ToString() } | Sort-Object)
    return [ordered]@{ Processes=@($identities | Sort-Object Pid); Listeners11434=$listeners }
}
function Invoke-AppCli([string]$Flag) {
    $info = [Diagnostics.ProcessStartInfo]::new($appExecutable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.ArgumentList.Add($Flag)
    $info.ArgumentList.Add('--lang')
    $info.ArgumentList.Add('en')
    $info.Environment['OLLAMA_HOST'] = "127.0.0.1:$Port"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    try {
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(110000)) { $process.Kill(); throw "Application $Flag exceeded 110 seconds" }
        $timer.Stop()
        $result = [ordered]@{ Utc=[DateTime]::UtcNow.ToString('O'); Flag=$Flag; ExitCode=$process.ExitCode; DurationMs=$timer.ElapsedMilliseconds; Stdout=$outTask.GetAwaiter().GetResult(); Stderr=$errTask.GetAwaiter().GetResult() }
        $cliResults.Add($result)
        Assert-Probe ($result.ExitCode -eq 0) "$Flag returns exit code 0 through the published app CLI"
        Assert-Probe ($result.Stdout.Contains($displayName)) "$Flag identifies the exact SCM fixture backend"
        return $result
    } finally { $process.Dispose() }
}
function Owned-ChildAlive($Identity) {
    if ($null -eq $Identity) { return $false }
    try { $process = Get-Process -Id $Identity.Pid -ErrorAction Stop } catch { return $false }
    try {
        if ($process.StartTime.ToUniversalTime().Ticks -ne $Identity.CreatedUtcTicks -or $process.Path -ne $Identity.Executable) { return $false }
        return !$process.HasExited
    } finally { $process.Dispose() }
}
function Read-HostEvents {
    if (!(Test-Path -LiteralPath $eventPath)) { return @() }
    return @(Get-Content -LiteralPath $eventPath | ForEach-Object { if ($_.Trim()) { $_ | ConvertFrom-Json } })
}
function Grant-FixtureAcl([string]$Path,[Security.AccessControl.FileSystemRights]$Rights) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if ($absolute -ne $fixtureDirectory -and !$absolute.StartsWith($fixtureDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'ACL target escaped fixture directory' }
    $acl = Get-Acl -LiteralPath $absolute
    $aclBackups.Add([pscustomobject]@{ Path=$absolute; Acl=$acl.GetSecurityDescriptorSddlForm([Security.AccessControl.AccessControlSections]::Access) })
    $sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-19')
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid,$Rights,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $absolute -AclObject $acl
}
function Save-Report {
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('O')
    $temporary = $reportPath + '.tmp'
    $report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $reportPath -Force
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    Assert-Probe ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Runner has an elevated Administrator token'
    Assert-Probe ((Test-Path -LiteralPath $hostExecutable) -and [IO.Path]::GetFileName($hostExecutable) -eq 'OllamaServiceFixture.exe') 'Native SCM fixture executable has been published'
    Assert-Probe ($report.AppVersion -eq $expectedFileVersion) "Tests the current published app version $expectedFileVersion"
    Assert-Probe ([IO.Path]::GetFileName($sourceOllama) -eq 'ollama.exe') 'Uses the genuine provided Ollama executable'
    $report.Windows = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture
    $report.Baseline = User-Snapshot
    Assert-Probe ($report.Baseline.Processes.Count -eq 0 -and $report.Baseline.Listeners11434.Count -eq 0) 'No existing Ollama/app processes or listener11434; fixture runs on an isolated runner'
    Assert-Probe (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -eq $Port).Count -eq 0) 'Private fixture port is unused'
    Assert-Probe ($null -eq (Get-FixtureService)) 'Exclusive randomized service name is not installed'
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceOllama -Destination $copiedOllama -Force
    $report.OllamaCopiedSha256 = (Get-FileHash -LiteralPath $copiedOllama -Algorithm SHA256).Hash
    Assert-Probe ($report.OllamaCopiedSha256 -eq $report.OllamaSourceSha256) 'Copied Ollama is byte identical to the supplied official binary'
    [ordered]@{ ServiceName=$serviceName; OllamaExe=$copiedOllama; RunDirectory=$runDirectory; Port=$Port } | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding utf8
    Grant-FixtureAcl $fixtureDirectory ([Security.AccessControl.FileSystemRights]::ReadAndExecute)
    Grant-FixtureAcl $runDirectory ([Security.AccessControl.FileSystemRights]::Modify)
    $binaryCommand = '"' + $hostExecutable + '" "' + $serviceName + '" "' + $configPath + '"'
    $report.Create = Invoke-Sc -Arguments @('create',$serviceName,'binPath=',$binaryCommand,'start=','demand','obj=','NT AUTHORITY\LocalService','DisplayName=',$displayName)
    $createdService = $true
    $report.Recovery = Invoke-Sc -Arguments @('failure',$serviceName,'reset=','0','actions=','restart/1000')
    $service = Get-FixtureService
    Assert-Probe ($service.State -eq 'Stopped' -and $service.StartMode -eq 'Manual' -and $service.StartName -eq 'NT AUTHORITY\LocalService') 'Manual LocalService fixture is initially stopped'
    Assert-Probe ($service.PathName -eq $binaryCommand) 'SCM binary command matches the exclusive fixture host/config'
    Invoke-AppCli '--status' | Out-Null
    for ($cycle=1; $cycle -le $Cycles; $cycle++) {
        $beforeEvents = @(Read-HostEvents | Where-Object { $_.Data.Event -eq 'child-start' }).Count
        $cycleReport = [ordered]@{ Cycle=$cycle; StartedUtc=[DateTime]::UtcNow.ToString('O'); Child=$null; ApiVersion=$null; StartMs=$null; StopMs=$null; RepeatedStartSameChild=$false; ChildExited=$false; ServiceStopped=$false; ApiClosed=$false; RecoveryDidNotRestart=$false }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        Invoke-AppCli '--on' | Out-Null
        Assert-Probe (Wait-Probe { (Get-FixtureService).State -eq 'Running' -and $null -ne (Api-Version) }) "Cycle $cycle SCM and Ollama API are running"
        $watch.Stop()
        $cycleReport.StartMs = $watch.ElapsedMilliseconds
        $cycleReport.ApiVersion = (Api-Version).version
        Assert-Probe ($cycleReport.ApiVersion -eq $ExpectedOllamaVersion) "Cycle $cycle API reports the pinned Ollama version $ExpectedOllamaVersion"
        $childStart = @(Read-HostEvents | Where-Object { $_.Data.Event -eq 'child-start' })[-1].Data
        $process = Get-Process -Id $childStart.Pid -ErrorAction Stop
        try { $cycleReport.Child = [ordered]@{ Pid=$process.Id; CreatedUtcTicks=$process.StartTime.ToUniversalTime().Ticks; CreationUtc=$process.StartTime.ToUniversalTime().ToString('O'); Executable=$process.Path } }
        finally { $process.Dispose() }
        Assert-Probe ($cycleReport.Child.Executable -eq $copiedOllama -and $childStart.Port -eq $Port -and (Owned-ChildAlive $cycleReport.Child)) "Cycle $cycle genuine Ollama identity and private port match"
        $tags = Invoke-RestMethod -Uri "$endpoint/api/tags" -TimeoutSec 3
        Assert-Probe (@($tags.models).Count -eq 0) "Cycle $cycle isolated models directory is empty"
        $listener = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop)
        Assert-Probe ($listener.Count -eq 1 -and $listener[0].OwningProcess -eq $cycleReport.Child.Pid) "Cycle $cycle API listener belongs to the retained Ollama identity"
        Invoke-AppCli '--on' | Out-Null
        $afterEvents = @(Read-HostEvents | Where-Object { $_.Data.Event -eq 'child-start' }).Count
        $cycleReport.RepeatedStartSameChild = $afterEvents -eq ($beforeEvents + 1) -and (Owned-ChildAlive $cycleReport.Child)
        Assert-Probe $cycleReport.RepeatedStartSameChild "Cycle $cycle repeated start keeps the same child"
        $watch.Restart()
        Invoke-AppCli '--off' | Out-Null
        Assert-Probe (Wait-Probe { (Service-IsStopped) -and !(Owned-ChildAlive $cycleReport.Child) -and $null -eq (Api-Version) }) "Cycle $cycle SCM stopped, original child exited, API closed"
        $watch.Stop()
        $cycleReport.StopMs = $watch.ElapsedMilliseconds
        $cycleReport.ChildExited = !(Owned-ChildAlive $cycleReport.Child)
        $cycleReport.ServiceStopped = Service-IsStopped
        $cycleReport.ApiClosed = $null -eq (Api-Version)
        Invoke-AppCli '--off' | Out-Null
        Start-Sleep -Seconds 3
        $cycleReport.RecoveryDidNotRestart = (Service-IsStopped) -and $null -eq (Api-Version) -and @(Read-HostEvents | Where-Object { $_.Data.Event -eq 'child-start' }).Count -eq $afterEvents
        Assert-Probe $cycleReport.RecoveryDidNotRestart "Cycle $cycle SCM stop does not trigger configured restart-on-failure"
        $report.Cycles += $cycleReport
    }
    if ($Cycles -ge 2) { Assert-Probe ($report.Cycles[0].Child.Pid -ne $report.Cycles[1].Child.Pid -or $report.Cycles[0].Child.CreatedUtcTicks -ne $report.Cycles[1].Child.CreatedUtcTicks) 'Second cycle starts a new process identity' }
    $report.Passed = $true
} catch {
    $report.Error = $_.ToString()
    Write-Host "FAIL: $($report.Error)"
} finally {
    $cleanup = [ordered]@{ Attempted=$createdService; ServiceStopped=$false; Deleted=$false; NoOwnedChildren=$false; ApiClosed=$false; AclsRestored=$false; BaselineUnchanged=$false; Error=$null }
    try {
        if ($createdService) {
            $ownedService = Get-FixtureService
            if ($null -ne $ownedService) {
                if ($ownedService.PathName -ne $binaryCommand) { throw 'Service identity changed; refusing unrelated service cleanup' }
                $cleanup.Stop = Invoke-Sc -Arguments @('stop',$serviceName) -AllowFailure
                Assert-Probe (Wait-Probe { Service-IsStopped }) 'Cleanup waits for the exclusive fixture service to stop'
                $cleanup.ServiceStopped = $true
                $cleanup.Delete = Invoke-Sc -Arguments @('delete',$serviceName)
                Assert-Probe (Wait-Probe { $null -eq (Get-FixtureService) }) 'Cleanup removes the exclusive fixture service'
            }
            $cleanup.Deleted = $null -eq (Get-FixtureService)
        }
        foreach ($event in (Read-HostEvents | Where-Object { $_.Data.Event -eq 'child-start' })) {
            $child = $event.Data
            try { $process = Get-Process -Id $child.Pid -ErrorAction Stop } catch { continue }
            try {
                $sameIdentity = $process.Path -eq $copiedOllama -and $process.StartTime.ToUniversalTime().ToString('O') -eq $child.CreationUtc
                if ($sameIdentity -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit(10000) | Out-Null }
            } finally { $process.Dispose() }
        }
        $cleanup.NoOwnedChildren = @(Get-Process -Name 'ollama' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $copiedOllama }).Count -eq 0
        $cleanup.ApiClosed = $null -eq (Api-Version)
        for ($index=$aclBackups.Count-1; $index -ge 0; $index--) {
            $entry = $aclBackups[$index]
            $acl = Get-Acl -LiteralPath $entry.Path
            $acl.SetSecurityDescriptorSddlForm($entry.Acl,[Security.AccessControl.AccessControlSections]::Access)
            Set-Acl -LiteralPath $entry.Path -AclObject $acl
        }
        $cleanup.AclsRestored = $true
        $report.FinalState = User-Snapshot
        $cleanup.BaselineUnchanged = ($report.Baseline | ConvertTo-Json -Depth 6 -Compress) -eq ($report.FinalState | ConvertTo-Json -Depth 6 -Compress)
        if ($createdService) {
            Assert-Probe ($cleanup.Deleted -and $cleanup.NoOwnedChildren -and $cleanup.ApiClosed -and $cleanup.BaselineUnchanged) 'Cleanup leaves no service, owned child or API and preserves baseline11434'
        }
    } catch { $cleanup.Error = $_.ToString(); $report.Passed = $false }
    $report.Cleanup = $cleanup
    $report.HostEvents = @(Read-HostEvents)
    Save-Report
    Write-Host "Evidence: $reportPath"
}
if (!$report.Passed) { throw "SCM verification failed; inspect $reportPath" }
