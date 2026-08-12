param(
    [int]$CountdownSeconds = 5,
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class QdcVoiceCalibrationNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    public static string WindowClass(IntPtr hwnd)
    {
        StringBuilder b = new StringBuilder(256);
        GetClassName(hwnd, b, b.Capacity);
        return b.ToString();
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
if ($CompileOnly) {
    Write-Host 'Qwen voice calibration helper compiled successfully.'
    exit 0
}

$target = $null
foreach ($process in [System.Diagnostics.Process]::GetProcessesByName('Qwen')) {
    try {
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and -not [string]::IsNullOrWhiteSpace($process.MainWindowTitle)) {
            if ($null -eq $target -or $process.MainWindowTitle -match 'Qwen') { $target = $process }
        }
    } catch {}
}

if ($null -eq $target) {
    throw 'No visible Qwen main window was found. Open Qwen Desktop first.'
}

$hwnd = $target.MainWindowHandle
$previousDpi = [IntPtr]::Zero
try {
    # Force this thread into the same physical-pixel coordinate space used by modern Chromium windows.
    # This avoids the Win10 DPI virtualization mismatch that can make GetCursorPos and client coordinates disagree.
    $previousDpi = [QdcVoiceCalibrationNative]::SetThreadDpiAwarenessContext([IntPtr](-4))

    $rect = New-Object QdcVoiceCalibrationNative+RECT
    if (-not [QdcVoiceCalibrationNative]::GetWindowRect($hwnd, [ref]$rect)) {
        throw 'GetWindowRect failed.'
    }

    Write-Host ''
    Write-Host 'VOICE CLICK CALIBRATION' -ForegroundColor Cyan
    Write-Host 'Move the mouse pointer directly onto the CENTER of the microphone/voice button in Qwen.'
    Write-Host 'Do not click. Keep the pointer there until the countdown finishes.'
    Write-Host 'If PowerShell covers the Qwen microphone button, move this PowerShell window aside first.'
    Write-Host ''
    for ($i = [Math]::Max(1, $CountdownSeconds); $i -ge 1; $i--) {
        Write-Host ("Capturing in {0}..." -f $i)
        Start-Sleep -Seconds 1
    }

    $cursor = New-Object QdcVoiceCalibrationNative+POINT
    if (-not [QdcVoiceCalibrationNative]::GetCursorPos([ref]$cursor)) {
        throw 'GetCursorPos failed.'
    }

    if ($cursor.X -lt $rect.Left -or $cursor.X -ge $rect.Right -or $cursor.Y -lt $rect.Top -or $cursor.Y -ge $rect.Bottom) {
        throw ("The mouse pointer was outside the Qwen window when calibration was captured. Cursor=({0},{1}), Qwen=({2},{3})-({4},{5}). Run again and keep the pointer on the microphone button." -f $cursor.X,$cursor.Y,$rect.Left,$rect.Top,$rect.Right,$rect.Bottom)
    }

    # Also verify the topmost window under the cursor belongs to Qwen; this catches a PowerShell/overlay window covering Qwen.
    $under = [QdcVoiceCalibrationNative]::WindowFromPoint($cursor)
    $rootUnder = if ($under -ne [IntPtr]::Zero) { [QdcVoiceCalibrationNative]::GetAncestor($under, 2) } else { [IntPtr]::Zero }
    if ($rootUnder -ne $hwnd) {
        throw ("The pointer coordinates are inside Qwen, but another window is covering that point (root HWND 0x{0:X}). Move PowerShell/other windows away from the microphone button and run calibration again." -f $rootUnder.ToInt64())
    }

    $offsetRight = [double]($rect.Right - $cursor.X)
    $offsetBottom = [double]($rect.Bottom - $cursor.Y)
    $windowClass = [QdcVoiceCalibrationNative]::WindowClass($hwnd)

    $root = Join-Path $env:LOCALAPPDATA 'QwenDesktopController'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $path = Join-Path $root 'voice-calibration.json'

    $result = [ordered]@{
        OffsetFromRight = $offsetRight
        OffsetFromBottom = $offsetBottom
        CoordinateSpace = 'window-screen-v2'
        WindowClass = $windowClass
        UpdatedAt = (Get-Date).ToUniversalTime().ToString('o')
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -Path $path

    Write-Host ''
    Write-Host 'Calibration saved.' -ForegroundColor Green
    Write-Host ("Qwen PID: {0}" -f $target.Id)
    Write-Host ("HWND: 0x{0:X}" -f $hwnd.ToInt64())
    Write-Host ("Cursor screen point: x={0}, y={1}" -f $cursor.X, $cursor.Y)
    Write-Host ("Qwen window rect: left={0}, top={1}, right={2}, bottom={3}" -f $rect.Left,$rect.Top,$rect.Right,$rect.Bottom)
    Write-Host ("Offsets: right={0}px, bottom={1}px" -f $offsetRight, $offsetBottom)
    Write-Host ("Saved to: {0}" -f $path)
    Write-Host 'No chat text, prompt text, credentials, cookies, audio, or screenshots were collected.'
}
finally {
    if ($previousDpi -ne [IntPtr]::Zero) {
        [void][QdcVoiceCalibrationNative]::SetThreadDpiAwarenessContext($previousDpi)
    }
}
