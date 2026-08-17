[CmdletBinding()]
param(
    [int]$DurationSeconds = 10,
    [int]$IntervalMilliseconds = 25
)

$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt 1 -or $DurationSeconds -gt 300) { throw 'DurationSeconds must be between 1 and 300.' }
if ($IntervalMilliseconds -lt 10 -or $IntervalMilliseconds -gt 1000) { throw 'IntervalMilliseconds must be between 10 and 1000.' }

if (-not ('ChatGPTAffinityWatch.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ChatGPTAffinityWatch
{
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    }
}
'@
}

function Find-ChatGPTMainWindow {
    $chatPids = [System.Collections.Generic.HashSet[uint32]]::new()
    Get-Process -Name 'ChatGPT Classic' -ErrorAction SilentlyContinue | ForEach-Object { [void]$chatPids.Add([uint32]$_.Id) }
    if ($chatPids.Count -eq 0) { return $null }

    $candidates = [System.Collections.Generic.List[object]]::new()
    $callback = [ChatGPTAffinityWatch.Native+EnumWindowsProc]{
        param([IntPtr]$hwnd, [IntPtr]$lParam)
        [uint32]$ownerPid = 0
        [void][ChatGPTAffinityWatch.Native]::GetWindowThreadProcessId($hwnd, [ref]$ownerPid)
        if (-not $chatPids.Contains($ownerPid) -or -not [ChatGPTAffinityWatch.Native]::IsWindowVisible($hwnd)) { return $true }

        $rect = New-Object ChatGPTAffinityWatch.Native+RECT
        if (-not [ChatGPTAffinityWatch.Native]::GetWindowRect($hwnd, [ref]$rect)) { return $true }
        $width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top
        if ($width -lt 320 -or $height -lt 200) { return $true }

        $className = New-Object System.Text.StringBuilder 256
        [void][ChatGPTAffinityWatch.Native]::GetClassName($hwnd, $className, $className.Capacity)
        $candidates.Add([pscustomobject]@{ Hwnd=$hwnd; Pid=$ownerPid; Class=$className.ToString(); Area=([int64]$width * [int64]$height) })
        return $true
    }
    [void][ChatGPTAffinityWatch.Native]::EnumWindows($callback, [IntPtr]::Zero)
    return $candidates | Sort-Object Area -Descending | Select-Object -First 1
}

$target = Find-ChatGPTMainWindow
if (-not $target) { throw 'No visible ChatGPT Classic main window was found.' }

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$deadline = [TimeSpan]::FromSeconds($DurationSeconds)
$lastState = $null
$longestUnprotectedMs = 0.0
$unprotectedSinceMs = $null
$samples = 0
$verifiedSamples = 0
$unverifiedSamples = 0

Write-Host ('Watching ChatGPT HWND 0x{0:X} PID {1} class {2} for {3}s at {4}ms intervals.' -f $target.Hwnd.ToInt64(), $target.Pid, $target.Class, $DurationSeconds, $IntervalMilliseconds)
Write-Host 'This script is read-only: it never sets affinity or changes window state.'

while ($stopwatch.Elapsed -lt $deadline) {
    if (-not [ChatGPTAffinityWatch.Native]::IsWindow($target.Hwnd)) {
        Write-Host ('{0,8:F1} ms  WINDOW_GONE' -f $stopwatch.Elapsed.TotalMilliseconds)
        break
    }

    [uint32]$affinity = 0
    $ok = [ChatGPTAffinityWatch.Native]::GetWindowDisplayAffinity($target.Hwnd, [ref]$affinity)
    $errorCode = if ($ok) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
    $verified = $ok -and $affinity -eq 0x11
    $state = if ($verified) { 'VERIFIED_0x11' } elseif ($ok) { 'EXPOSED_0x{0:X}' -f $affinity } else { 'UNREADABLE_ERR_{0}' -f $errorCode }
    $elapsedMs = $stopwatch.Elapsed.TotalMilliseconds
    $samples++

    if ($verified) {
        $verifiedSamples++
        if ($null -ne $unprotectedSinceMs) {
            $gap = $elapsedMs - $unprotectedSinceMs
            if ($gap -gt $longestUnprotectedMs) { $longestUnprotectedMs = $gap }
            $unprotectedSinceMs = $null
        }
    }
    else {
        $unverifiedSamples++
        if ($null -eq $unprotectedSinceMs) { $unprotectedSinceMs = $elapsedMs }
    }

    if ($state -ne $lastState) {
        Write-Host ('{0,8:F1} ms  {1}' -f $elapsedMs, $state)
        $lastState = $state
    }

    Start-Sleep -Milliseconds $IntervalMilliseconds
}

if ($null -ne $unprotectedSinceMs) {
    $gap = $stopwatch.Elapsed.TotalMilliseconds - $unprotectedSinceMs
    if ($gap -gt $longestUnprotectedMs) { $longestUnprotectedMs = $gap }
}

Write-Host ('SUMMARY samples={0} verified={1} unverified={2} longestUnverifiedMs={3:F1}' -f $samples, $verifiedSamples, $unverifiedSamples, $longestUnprotectedMs)
