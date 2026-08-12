param(
    [int]$CountdownSeconds = 5
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
    public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    public static string WindowClass(IntPtr hwnd)
    {
        StringBuilder b = new StringBuilder(256);
        GetClassName(hwnd, b, b.Capacity);
        return b.ToString();
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp

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
$rect = New-Object QdcVoiceCalibrationNative+RECT
if (-not [QdcVoiceCalibrationNative]::GetClientRect($hwnd, [ref]$rect)) {
    throw 'GetClientRect failed.'
}

Write-Host ''
Write-Host 'VOICE CLICK CALIBRATION' -ForegroundColor Cyan
Write-Host 'Move the mouse pointer directly onto the CENTER of the microphone/voice button in Qwen.'
Write-Host 'Do not click. Keep the pointer there until the countdown finishes.'
Write-Host ''
for ($i = [Math]::Max(1, $CountdownSeconds); $i -ge 1; $i--) {
    Write-Host ("Capturing in {0}..." -f $i)
    Start-Sleep -Seconds 1
}

$screen = New-Object QdcVoiceCalibrationNative+POINT
if (-not [QdcVoiceCalibrationNative]::GetCursorPos([ref]$screen)) {
    throw 'GetCursorPos failed.'
}
$client = $screen
if (-not [QdcVoiceCalibrationNative]::ScreenToClient($hwnd, [ref]$client)) {
    throw 'ScreenToClient failed.'
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($client.X -lt 0 -or $client.Y -lt 0 -or $client.X -ge $width -or $client.Y -ge $height) {
    throw 'The mouse pointer was not inside the Qwen client area when calibration was captured.'
}

$offsetRight = [double]($width - $client.X)
$offsetBottom = [double]($height - $client.Y)
$windowClass = [QdcVoiceCalibrationNative]::WindowClass($hwnd)

$root = Join-Path $env:LOCALAPPDATA 'QwenDesktopController'
New-Item -ItemType Directory -Force -Path $root | Out-Null
$path = Join-Path $root 'voice-calibration.json'

$result = [ordered]@{
    OffsetFromRight = $offsetRight
    OffsetFromBottom = $offsetBottom
    WindowClass = $windowClass
    UpdatedAt = (Get-Date).ToUniversalTime().ToString('o')
}
$result | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -Path $path

Write-Host ''
Write-Host 'Calibration saved.' -ForegroundColor Green
Write-Host ("Qwen PID: {0}" -f $target.Id)
Write-Host ("HWND: 0x{0:X}" -f $hwnd.ToInt64())
Write-Host ("Client point: x={0}, y={1}" -f $client.X, $client.Y)
Write-Host ("Offsets: right={0}px, bottom={1}px" -f $offsetRight, $offsetBottom)
Write-Host ("Saved to: {0}" -f $path)
Write-Host 'No chat text, prompt text, credentials, cookies, audio, or screenshots were collected.'
