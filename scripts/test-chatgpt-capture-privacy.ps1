[CmdletBinding()]
param(
    [switch]$FailOnExposure
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('ChatGPTCaptureProbe.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace ChatGPTCaptureProbe
{
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }
}
'@
}

$chatPids = [System.Collections.Generic.HashSet[uint32]]::new()
Get-Process -Name 'ChatGPT Classic' -ErrorAction SilentlyContinue | ForEach-Object { [void]$chatPids.Add([uint32]$_.Id) }
if ($chatPids.Count -eq 0) { throw 'ChatGPT Classic is not running.' }

$candidates = [System.Collections.Generic.List[object]]::new()
$callback = [ChatGPTCaptureProbe.Native+EnumWindowsProc]{
    param([IntPtr]$hwnd, [IntPtr]$lParam)
    [uint32]$pid = 0
    [void][ChatGPTCaptureProbe.Native]::GetWindowThreadProcessId($hwnd, [ref]$pid)
    if (-not $chatPids.Contains($pid) -or -not [ChatGPTCaptureProbe.Native]::IsWindowVisible($hwnd)) { return $true }
    $rect = New-Object ChatGPTCaptureProbe.Native+RECT
    if (-not [ChatGPTCaptureProbe.Native]::GetWindowRect($hwnd, [ref]$rect)) { return $true }
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -lt 320 -or $h -lt 200) { return $true }
    $candidates.Add([pscustomobject]@{ Hwnd=$hwnd; Pid=$pid; Rect=$rect; Area=([int64]$w * [int64]$h) })
    return $true
}
[void][ChatGPTCaptureProbe.Native]::EnumWindows($callback, [IntPtr]::Zero)
if ($candidates.Count -eq 0) { throw 'No visible ChatGPT Classic top-level window was found.' }
$target = $candidates | Sort-Object Area -Descending | Select-Object -First 1
$rect = $target.Rect
$width = $rect.Right - $rect.Left; $height = $rect.Bottom - $rect.Top

[uint32]$affinity = 0
$affinityReadable = [ChatGPTCaptureProbe.Native]::GetWindowDisplayAffinity($target.Hwnd, [ref]$affinity)
$affinityError = if ($affinityReadable) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }

$screen = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$screenGraphics = [System.Drawing.Graphics]::FromImage($screen)
try {
    $screenGraphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height), [System.Drawing.CopyPixelOperation]::SourceCopy)
}
finally { $screenGraphics.Dispose() }

$direct = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$directGraphics = [System.Drawing.Graphics]::FromImage($direct)
$hdc = [IntPtr]::Zero
try {
    $hdc = $directGraphics.GetHdc()
    $printOk = [ChatGPTCaptureProbe.Native]::PrintWindow($target.Hwnd, $hdc, 2)
}
finally {
    if ($hdc -ne [IntPtr]::Zero) { $directGraphics.ReleaseHdc($hdc) }
    $directGraphics.Dispose()
}

function Get-SampleStats([System.Drawing.Bitmap]$a, [System.Drawing.Bitmap]$b) {
    $grid = 24
    $values = New-Object System.Collections.Generic.List[double]
    $diff = 0.0
    for ($y = 0; $y -lt $grid; $y++) {
        for ($x = 0; $x -lt $grid; $x++) {
            $px = [Math]::Min($a.Width - 1, [int](($x + 0.5) * $a.Width / $grid))
            $py = [Math]::Min($a.Height - 1, [int](($y + 0.5) * $a.Height / $grid))
            $ca = $a.GetPixel($px, $py); $cb = $b.GetPixel($px, $py)
            $lum = ($cb.R + $cb.G + $cb.B) / 3.0
            $values.Add($lum)
            $diff += ([Math]::Abs([int]$ca.R - [int]$cb.R) + [Math]::Abs([int]$ca.G - [int]$cb.G) + [Math]::Abs([int]$ca.B - [int]$cb.B)) / 3.0
        }
    }
    $mean = ($values | Measure-Object -Average).Average
    $variance = 0.0
    foreach ($v in $values) { $variance += ($v - $mean) * ($v - $mean) }
    $variance /= [Math]::Max(1, $values.Count)
    $diff /= [Math]::Max(1, $values.Count)
    [pscustomobject]@{ Difference=$diff; DirectVariance=$variance }
}

try {
    if (-not $printOk) {
        $verdict = 'INCONCLUSIVE'
        $stats = [pscustomobject]@{ Difference=[double]::NaN; DirectVariance=[double]::NaN }
        $detail = 'PrintWindow returned FALSE, so screen/direct comparison is unavailable.'
    }
    else {
        $stats = Get-SampleStats $screen $direct
        if ($stats.DirectVariance -lt 6) {
            $verdict = 'INCONCLUSIVE'
            $detail = 'Direct window sample is too uniform to identify ChatGPT content reliably.'
        }
        elseif ($stats.Difference -le 8) {
            $verdict = 'EXPOSED'
            $detail = 'Screen capture closely matches the direct ChatGPT window render.'
        }
        elseif ($stats.Difference -ge 18) {
            $verdict = 'LIKELY_EXCLUDED'
            $detail = 'Screen capture differs strongly from the direct ChatGPT window render.'
        }
        else {
            $verdict = 'INCONCLUSIVE'
            $detail = 'Screen/direct difference is in the ambiguous range.'
        }
    }

    $affinityText = if ($affinityReadable) { ('0x{0:X}' -f $affinity) } else { "unreadable(win32=$affinityError)" }
    Write-Host ('ChatGPT HWND: 0x{0:X} PID: {1} Size: {2}x{3}' -f $target.Hwnd.ToInt64(), $target.Pid, $width, $height)
    Write-Host "DisplayAffinity: $affinityText"
    Write-Host ('GDI-vs-PrintWindow: {0} Difference={1:F1} DirectVariance={2:F1}' -f $verdict, $stats.Difference, $stats.DirectVariance)
    Write-Host "Detail: $detail"
    Write-Host 'No screenshot files were written and the ChatGPT window state was not changed.'

    if ($FailOnExposure -and $verdict -eq 'EXPOSED') { exit 2 }
}
finally {
    $screen.Dispose(); $direct.Dispose()
}
