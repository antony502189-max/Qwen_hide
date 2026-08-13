$ErrorActionPreference = 'Continue'
Write-Host 'Qwen Desktop Controller diagnostics'
Write-Host "Windows: $([Environment]::OSVersion.VersionString)"
Write-Host "User: $env:USERNAME"
Write-Host "Machine: $env:COMPUTERNAME"

Write-Host ''
Write-Host 'Running Qwen-like processes:'
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'qwen' } |
    ForEach-Object {
        $path = $null
        try { $path = $_.Path } catch {}
        [pscustomobject]@{ PID=$_.Id; Process=$_.ProcessName; MainWindowTitle=$_.MainWindowTitle; Path=$path }
    } | Format-Table -AutoSize

Write-Host ''
Write-Host 'Common Qwen executable candidates:'
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Qwen\Qwen.exe'),
    (Join-Path $env:LOCALAPPDATA 'Qwen\Qwen.exe'),
    (Join-Path $env:ProgramFiles 'Qwen\Qwen.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Qwen\Qwen.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$candidates | ForEach-Object { Write-Host "  $_" }
if (-not $candidates) { Write-Host '  No common-path candidate found. Open Qwen normally; the controller can attach to the running process.' }

Add-Type -AssemblyName System.Windows.Forms
Write-Host ''
Write-Host 'Screens:'
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object { Write-Host "  $($_.DeviceName) $($_.Bounds)" }

Write-Host ''
Write-Host 'Audio note:'
Write-Host '  The controller uses shared-mode capture and does not change Windows default audio devices.'
Write-Host '  Audio endpoint names and selected IDs are shown in the controller Settings/Diagnostics UI.'
Write-Host ''
Write-Host 'Capture privacy note:'
Write-Host '  The controller never applies WDA_EXCLUDEFROMCAPTURE directly to Qwen''s foreign HWND.'
Write-Host '  Its optional Privacy Host applies and reads back WDA on a controller-owned top-level HWND before hosting real Qwen.'
Write-Host '  Host affinity verification is not a guarantee for GDI, Desktop Duplication, Windows Graphics Capture, or conferencing apps.'
Write-Host '  Capture privacy is unsupported for the installed native Qwen window on this target; do not infer full-monitor exclusion.'
