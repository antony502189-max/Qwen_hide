param(
    [string]$ControllerPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist-single\QwenDesktopController.exe'),
    [int]$DurationSeconds = 60,
    [switch]$SafeMode
)

$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt 10) { throw 'DurationSeconds must be at least 10.' }
if (-not (Test-Path $ControllerPath)) { throw "Controller executable not found: $ControllerPath" }

function Get-Sample([System.Diagnostics.Process]$Process, [TimeSpan]$CpuStart, [DateTime]$WallStart) {
    $Process.Refresh()
    $wall = ((Get-Date) - $WallStart).TotalSeconds
    $cpu = if ($wall -gt 0) { (($Process.TotalProcessorTime - $CpuStart).TotalSeconds / ($wall * [Environment]::ProcessorCount)) * 100 } else { 0 }
    [pscustomobject]@{
        Timestamp = (Get-Date).ToString('o')
        CpuPercent = [Math]::Round($cpu, 3)
        WorkingSetMiB = [Math]::Round($Process.WorkingSet64 / 1MB, 2)
        Threads = $Process.Threads.Count
        Handles = $Process.HandleCount
    }
}

$arguments = if ($SafeMode) { "--safe-mode --exit-after-seconds $($DurationSeconds + 5)" } else { '' }
$process = Start-Process -FilePath $ControllerPath -ArgumentList $arguments -PassThru
Start-Sleep -Seconds 2
if ($process.HasExited) { throw "Controller exited immediately with code $($process.ExitCode). A single-instance controller may already be running." }

$cpuStart = $process.TotalProcessorTime
$wallStart = Get-Date
$samples = @()
try {
    for ($i = 0; $i -lt $DurationSeconds; $i++) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { throw "Controller exited during measurement with code $($process.ExitCode)." }
        $samples += Get-Sample $process $cpuStart $wallStart
    }
}
finally { if (-not $process.HasExited) { $process.WaitForExit(10000) } }

$report = [pscustomobject]@{
    ControllerPath = (Resolve-Path $ControllerPath).Path
    SafeMode = [bool]$SafeMode
    DurationSeconds = $DurationSeconds
    AverageCpuPercent = [Math]::Round((($samples | Measure-Object CpuPercent -Average).Average), 3)
    PeakCpuPercent = [Math]::Round((($samples | Measure-Object CpuPercent -Maximum).Maximum), 3)
    PeakWorkingSetMiB = [Math]::Round((($samples | Measure-Object WorkingSetMiB -Maximum).Maximum), 2)
    PeakThreads = ($samples | Measure-Object Threads -Maximum).Maximum
    PeakHandles = ($samples | Measure-Object Handles -Maximum).Maximum
    Samples = $samples
}
$reportPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QwenDesktopController\performance-report.json'
$directory = Split-Path -Parent $reportPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$report | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $reportPath
$report | Select-Object ControllerPath,SafeMode,DurationSeconds,AverageCpuPercent,PeakCpuPercent,PeakWorkingSetMiB,PeakThreads,PeakHandles | Format-List
Write-Host "Saved: $reportPath"
