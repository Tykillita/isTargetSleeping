param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'obj/backend-verification/task')
)
$ErrorActionPreference = 'Stop'
$stopCleanupError = $null
$testRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$allowedOutputRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path 'obj')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$testRoot.StartsWith($allowedOutputRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be inside the workspace obj directory' }
$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
if (!$resolvedManifest.StartsWith($testRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest outside verification workspace' }
$manifest = Get-Content -LiteralPath $resolvedManifest -Raw | ConvertFrom-Json
if ($manifest.TaskName -notmatch '^isTargetSleeping-BackendVerification-Task-[a-f0-9]{8}$' -or $manifest.TaskPath -ne ('\' + $manifest.TaskName)) { throw 'Unexpected task identity' }
$expectedRunRoot = Join-Path $testRoot $manifest.TaskName
if ($manifest.RunRoot -ne $expectedRunRoot -or $manifest.Wrapper -ne (Join-Path $expectedRunRoot 'ollama-serve-task.ps1') -or $resolvedManifest -ne (Join-Path $expectedRunRoot 'manifest.json')) { throw 'Unexpected fixture paths' }
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
try { $task = $folder.GetTask($manifest.TaskPath) } catch { $task = $null }
if ($null -ne $task) {
    foreach ($action in $task.Definition.Actions) {
        if ($action.Arguments -notlike ('*' + $manifest.Wrapper + '*')) { throw 'Task action does not match manifest' }
    }
    try { $task.Stop(0) } catch { $stopCleanupError = $_.Exception.Message }
    $folder.DeleteTask($manifest.TaskName,0)
}
$childPath = Join-Path $manifest.RunRoot 'child.json'
if (Test-Path -LiteralPath $childPath) {
    $child = Get-Content -LiteralPath $childPath -Raw | ConvertFrom-Json
    foreach ($identity in @($child.Wrapper,$child.Auxiliary,$child)) {
        if ($null -eq $identity) { continue }
        try { $process = Get-Process -Id $identity.Pid -ErrorAction Stop } catch { $process = $null }
        if ($null -ne $process) {
            try {
                if ($process.StartTime.ToUniversalTime().Ticks -eq $identity.CreatedUtcTicks -and $process.Path -eq $identity.Executable) {
                    $process.Kill($true)
                    if (!$process.WaitForExit(10000)) { throw 'Verification process still alive' }
                } else { throw 'Child identity changed; refusing to kill' }
            } finally { $process.Dispose() }
        }
    }
}
try { $remainingTask = $folder.GetTask($manifest.TaskPath) } catch { $remainingTask = $null }
if ($null -ne $remainingTask) { throw 'Verification task still registered' }
[ordered]@{ TaskRemoved=$true; TaskPath=$manifest.TaskPath; StopCleanupError=$stopCleanupError; CompletedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json
