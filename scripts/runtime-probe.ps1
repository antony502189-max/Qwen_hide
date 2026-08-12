param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = $PSScriptRoot
$root = if ((Split-Path $scriptDirectory -Leaf) -ieq 'scripts') {
    Split-Path -Parent $scriptDirectory
} else {
    $scriptDirectory
}
if (-not $OutputPath) {
    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $OutputPath = Join-Path $artifacts 'runtime-probe.json'
}

# Keep the embedded C# compatible with the legacy compiler used by Windows PowerShell 5.1.
# In particular, avoid C# 6+ expression-bodied members here.
if (-not ('QdcProbeNative' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class QdcProbeNative {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint="GetWindowLongW", SetLastError=true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    public static long GetExStyle(IntPtr hWnd) {
        if (IntPtr.Size == 8) {
            return GetWindowLongPtr64(hWnd, -20).ToInt64();
        }
        return GetWindowLong32(hWnd, -20);
    }

    public static string Text(IntPtr hWnd) {
        StringBuilder b = new StringBuilder(1024);
        GetWindowTextW(hWnd, b, b.Capacity);
        return b.ToString();
    }

    public static string Class(IntPtr hWnd) {
        StringBuilder b = new StringBuilder(512);
        GetClassNameW(hWnd, b, b.Capacity);
        return b.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@
}

function Get-QwenWindows([int]$ProcessId) {
    $items = New-Object 'System.Collections.Generic.List[object]'
    $callback = [QdcProbeNative+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        [uint32]$ownerPid = 0
        [void][QdcProbeNative]::GetWindowThreadProcessId($hWnd, [ref]$ownerPid)
        if ($ownerPid -eq $ProcessId) {
            $rect = New-Object QdcProbeNative+RECT
            [void][QdcProbeNative]::GetWindowRect($hWnd, [ref]$rect)
            $items.Add([pscustomobject]@{
                hwnd = ('0x{0:X}' -f $hWnd.ToInt64())
                title = [QdcProbeNative]::Text($hWnd)
                class = [QdcProbeNative]::Class($hWnd)
                visible = [QdcProbeNative]::IsWindowVisible($hWnd)
                minimized = [QdcProbeNative]::IsIconic($hWnd)
                maximized = [QdcProbeNative]::IsZoomed($hWnd)
                exStyle = ('0x{0:X}' -f [QdcProbeNative]::GetExStyle($hWnd))
                rect = [pscustomobject]@{ left=$rect.Left; top=$rect.Top; right=$rect.Right; bottom=$rect.Bottom }
            })
        }
        return $true
    }
    [void][QdcProbeNative]::EnumWindows($callback, [IntPtr]::Zero)
    return @($items)
}

$qwen = @()
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'qwen' } | ForEach-Object {
    $p = $_
    $path = $null
    $version = $null
    $signature = $null
    $modules = @()
    try { $path = $p.Path } catch {}
    if ($path) {
        try {
            $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
            $version = [pscustomobject]@{ file=$vi.FileVersion; product=$vi.ProductVersion; productName=$vi.ProductName; company=$vi.CompanyName }
        } catch {}
        try {
            $sig = Get-AuthenticodeSignature -FilePath $path
            $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null }
            $signature = [pscustomobject]@{ status=$sig.Status.ToString(); signer=$signer }
        } catch {}
    }
    try {
        $modules = @($p.Modules | Select-Object -ExpandProperty ModuleName | Sort-Object -Unique)
    } catch {}

    $qwen += [pscustomobject]@{
        pid = $p.Id
        processName = $p.ProcessName
        mainWindowTitle = $p.MainWindowTitle
        executable = $path
        version = $version
        signature = $signature
        frameworkHints = @($modules | Where-Object { $_ -match 'electron|chrome|cef|webview|qt|angle|v8|node' })
        windows = @(Get-QwenWindows -ProcessId $p.Id)
    }
}

$controller = @()
Get-Process -Name 'QwenDesktopController' -ErrorAction SilentlyContinue | ForEach-Object {
    $controllerPath = $null
    try { $controllerPath = $_.Path } catch {}
    $controller += [pscustomobject]@{ pid=$_.Id; mainWindowTitle=$_.MainWindowTitle; path=$controllerPath }
}

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    os = [Environment]::OSVersion.VersionString
    osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    machine = $env:COMPUTERNAME
    qwenProcesses = $qwen
    controllerProcesses = $controller
    notes = @(
        'No command lines, chat text, clipboard content, cookies, tokens, passwords or audio data are collected.',
        'Use this file to validate the exact native Qwen PID/HWND/window class/framework on the target PC.'
    )
}

$result | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -Path $OutputPath
Write-Host "Runtime probe written to: $OutputPath"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
Write-Host "Qwen-like processes found: $($qwen.Count)"
if ($qwen.Count -eq 0) { Write-Warning 'Open the installed Qwen Desktop app and run this probe again.' }
