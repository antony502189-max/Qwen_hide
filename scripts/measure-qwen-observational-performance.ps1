param(
    [string]$ControllerPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist-single\QwenDesktopController.exe'),
    [int]$DurationSeconds = 120
)

$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt 10) { throw 'DurationSeconds must be at least 10.' }
if (-not (Test-Path $ControllerPath)) { throw "Controller executable not found: $ControllerPath" }
if (Get-Process QwenDesktopController -ErrorAction SilentlyContinue) { throw 'Stop any existing QwenDesktopController before measuring.' }

$root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QwenDesktopController'
$journal = Join-Path $root 'window-recovery.json'
if (Test-Path $journal) { throw "Recovery journal exists: $journal. Restore Qwen before measuring." }

$installedQwen = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Qwen\Qwen.exe'
$qwenCandidates = @(Get-Process Qwen -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and [string]::Equals($_.Path, $installedQwen, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
} | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1)
if ($qwenCandidates.Count -ne 1) { throw "Exactly one visible installed Qwen process is required; found $($qwenCandidates.Count)." }
$qwen = $qwenCandidates[0]

function Get-CpuPercent([System.Diagnostics.Process]$Process, [TimeSpan]$cpuStart, [DateTime]$wallStart) {
    $Process.Refresh()
    $elapsed = ((Get-Date) - $wallStart).TotalSeconds
    if ($elapsed -le 0) { return 0.0 }
    return (($Process.TotalProcessorTime - $cpuStart).TotalSeconds / ($elapsed * [Environment]::ProcessorCount)) * 100
}

function Measure-Qwen([System.Diagnostics.Process]$Process, [int]$Seconds) {
    $cpuStart = $Process.TotalProcessorTime
    $wallStart = Get-Date
    $samples = @()
    for ($i = 0; $i -lt $Seconds; $i++) {
        Start-Sleep -Seconds 1
        if ($Process.HasExited) { throw 'Qwen exited during measurement.' }
        $Process.Refresh()
        $samples += [pscustomobject]@{
            Timestamp = (Get-Date).ToString('o')
            CpuPercent = [Math]::Round((Get-CpuPercent $Process $cpuStart $wallStart), 3)
            WorkingSetMiB = [Math]::Round($Process.WorkingSet64 / 1MB, 2)
            Responding = $Process.Responding
        }
    }
    return $samples
}

$baselineSeconds = [Math]::Max(10, [Math]::Min(30, [Math]::Floor($DurationSeconds / 2)))
$baseline = Measure-Qwen $qwen $baselineSeconds
$controller = $null
try {
    $controller = Start-Process -FilePath $ControllerPath -PassThru
    Start-Sleep -Seconds 3
    if ($controller.HasExited) { throw "Controller exited immediately with code $($controller.ExitCode)." }
    if (Test-Path $journal) { throw 'Observational startup unexpectedly created a recovery journal.' }
    $attached = Measure-Qwen $qwen $DurationSeconds
}
finally {
    # The release starts observationally and creates no journal without an explicit mutation.
    # Do not force-stop a controller that has acquired recovery state.
    if ($controller -and -not $controller.HasExited) {
        if (Test-Path $journal) {
            Write-Warning 'Controller left running because a recovery journal appeared during the observational measurement.'
        }
        else {
            Stop-Process -Id $controller.Id -ErrorAction SilentlyContinue
            $controller.WaitForExit(5000) | Out-Null
        }
    }
}

$result = [pscustomobject]@{
    ControllerPath = (Resolve-Path $ControllerPath).Path
    QwenPid = $qwen.Id
    QwenExecutable = $installedQwen
    BaselineSeconds = $baselineSeconds
    AttachedSeconds = $DurationSeconds
    BaselineAverageCpuPercent = [Math]::Round((($baseline | Measure-Object CpuPercent -Average).Average), 3)
    AttachedAverageCpuPercent = [Math]::Round((($attached | Measure-Object CpuPercent -Average).Average), 3)
    CpuDeltaPercentagePoints = [Math]::Round((($attached | Measure-Object CpuPercent -Average).Average - ($baseline | Measure-Object CpuPercent -Average).Average), 3)
    BaselinePeakWorkingSetMiB = [Math]::Round((($baseline | Measure-Object WorkingSetMiB -Maximum).Maximum), 2)
    AttachedPeakWorkingSetMiB = [Math]::Round((($attached | Measure-Object WorkingSetMiB -Maximum).Maximum), 2)
    AllQwenSamplesResponding = -not (@($baseline + $attached | Where-Object { -not $_.Responding }).Count)
    RecoveryJournalExistsAfter = Test-Path $journal
    BaselineSamples = $baseline
    AttachedSamples = $attached
}

if ($result.RecoveryJournalExistsAfter) { throw 'Recovery journal exists after observational measurement.' }
if (-not $result.AllQwenSamplesResponding) { throw 'Qwen became unresponsive during observational measurement.' }

New-Item -ItemType Directory -Path $root -Force | Out-Null
$path = Join-Path $root 'qwen-observational-performance.json'
$result | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $path
$result | Select-Object ControllerPath,QwenPid,BaselineSeconds,AttachedSeconds,BaselineAverageCpuPercent,AttachedAverageCpuPercent,CpuDeltaPercentagePoints,BaselinePeakWorkingSetMiB,AttachedPeakWorkingSetMiB,AllQwenSamplesResponding,RecoveryJournalExistsAfter | Format-List
Write-Host "Saved: $path"
