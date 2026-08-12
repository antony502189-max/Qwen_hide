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

# Keep the embedded C# compatible with the legacy CodeDOM compiler used by Windows PowerShell 5.1.
# Window enumeration is performed entirely in C# instead of passing a PowerShell scriptblock as
# an EnumWindows callback. The latter is unreliable on Windows PowerShell 5.1 and can fail with
# "Argument types do not match" even though it works in PowerShell 7.
if (-not ('QdcProbeNative' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public sealed class QdcWindowInfo {
    public long Hwnd;
    public string Title;
    public string ClassName;
    public bool Visible;
    public bool Minimized;
    public bool Maximized;
    public long ExStyle;
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public static class QdcProbeNative {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static uint _targetPid;
    private static List<QdcWindowInfo> _windows;

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError=true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint="GetWindowLongW", SetLastError=true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    private static long GetExStyle(IntPtr hWnd) {
        if (IntPtr.Size == 8) {
            return GetWindowLongPtr64(hWnd, -20).ToInt64();
        }
        return GetWindowLong32(hWnd, -20);
    }

    private static string GetText(IntPtr hWnd) {
        StringBuilder b = new StringBuilder(1024);
        GetWindowTextW(hWnd, b, b.Capacity);
        return b.ToString();
    }

    private static string GetClass(IntPtr hWnd) {
        StringBuilder b = new StringBuilder(512);
        GetClassNameW(hWnd, b, b.Capacity);
        return b.ToString();
    }

    private static bool CollectWindow(IntPtr hWnd, IntPtr lParam) {
        uint ownerPid;
        GetWindowThreadProcessId(hWnd, out ownerPid);
        if (ownerPid != _targetPid) {
            return true;
        }

        RECT rect;
        GetWindowRect(hWnd, out rect);
        QdcWindowInfo info = new QdcWindowInfo();
        info.Hwnd = hWnd.ToInt64();
        info.Title = GetText(hWnd);
        info.ClassName = GetClass(hWnd);
        info.Visible = IsWindowVisible(hWnd);
        info.Minimized = IsIconic(hWnd);
        info.Maximized = IsZoomed(hWnd);
        info.ExStyle = GetExStyle(hWnd);
        info.Left = rect.Left;
        info.Top = rect.Top;
        info.Right = rect.Right;
        info.Bottom = rect.Bottom;
        _windows.Add(info);
        return true;
    }

    public static QdcWindowInfo[] EnumerateProcessWindows(uint processId) {
        _targetPid = processId;
        _windows = new List<QdcWindowInfo>();
        EnumWindowsProc callback = new EnumWindowsProc(CollectWindow);
        if (!EnumWindows(callback, IntPtr.Zero)) {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException("EnumWindows failed. Win32 error: " + error.ToString());
        }
        QdcWindowInfo[] result = _windows.ToArray();
        _windows = null;
        _targetPid = 0;
        GC.KeepAlive(callback);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@
}

function Get-QwenWindows([int]$ProcessId) {
    $result = New-Object System.Collections.ArrayList
    $nativeItems = [QdcProbeNative]::EnumerateProcessWindows([uint32]$ProcessId)
    foreach ($item in $nativeItems) {
        $windowInfo = [pscustomobject]@{
            hwnd = ('0x{0:X}' -f $item.Hwnd)
            title = $item.Title
            class = $item.ClassName
            visible = $item.Visible
            minimized = $item.Minimized
            maximized = $item.Maximized
            exStyle = ('0x{0:X}' -f $item.ExStyle)
            rect = [pscustomobject]@{
                left = $item.Left
                top = $item.Top
                right = $item.Right
                bottom = $item.Bottom
            }
        }
        [void]$result.Add($windowInfo)
    }
    return $result.ToArray()
}

# Avoid PowerShell pipeline-based process enumeration here. Windows PowerShell 5.1 can surface
# "Argument types do not match" from ForEach-Object when native/.NET process objects expose
# transient or framework-specific members. Plain .NET enumeration plus foreach is more robust.
$qwenList = New-Object System.Collections.ArrayList
$allProcesses = [System.Diagnostics.Process]::GetProcesses()
foreach ($p in $allProcesses) {
    $processName = $null
    try { $processName = $p.ProcessName } catch { continue }
    if ([string]::IsNullOrWhiteSpace($processName) -or $processName -notmatch 'qwen') { continue }

    $path = $null
    $version = $null
    $signature = $null
    $mainWindowTitle = $null
    $frameworkHintsList = New-Object System.Collections.ArrayList

    try { $mainWindowTitle = $p.MainWindowTitle } catch {}
    try { $path = $p.MainModule.FileName } catch {}

    if ($path) {
        try {
            $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
            $version = [pscustomobject]@{
                file = $vi.FileVersion
                product = $vi.ProductVersion
                productName = $vi.ProductName
                company = $vi.CompanyName
            }
        } catch {}
        try {
            $sig = Get-AuthenticodeSignature -FilePath $path
            $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { $null }
            $signature = [pscustomobject]@{ status=$sig.Status.ToString(); signer=$signer }
        } catch {}
    }

    try {
        foreach ($module in $p.Modules) {
            $moduleName = $null
            try { $moduleName = $module.ModuleName } catch {}
            if ($moduleName -and $moduleName -match 'electron|chrome|cef|webview|qt|angle|v8|node') {
                if (-not $frameworkHintsList.Contains($moduleName)) {
                    [void]$frameworkHintsList.Add($moduleName)
                }
            }
        }
    } catch {}

    $windows = @()
    try { $windows = @(Get-QwenWindows -ProcessId $p.Id) } catch {
        $windows = @([pscustomobject]@{ error = $_.Exception.Message })
    }

    $entry = [pscustomobject]@{
        pid = $p.Id
        processName = $processName
        mainWindowTitle = $mainWindowTitle
        executable = $path
        version = $version
        signature = $signature
        frameworkHints = $frameworkHintsList.ToArray()
        windows = $windows
    }
    [void]$qwenList.Add($entry)
}
$qwen = $qwenList.ToArray()

$controllerList = New-Object System.Collections.ArrayList
foreach ($p in [System.Diagnostics.Process]::GetProcessesByName('QwenDesktopController')) {
    $controllerPath = $null
    $controllerTitle = $null
    try { $controllerPath = $p.MainModule.FileName } catch {}
    try { $controllerTitle = $p.MainWindowTitle } catch {}
    [void]$controllerList.Add([pscustomobject]@{
        pid = $p.Id
        mainWindowTitle = $controllerTitle
        path = $controllerPath
    })
}
$controller = $controllerList.ToArray()

$osArch = $env:PROCESSOR_ARCHITECTURE
$processArch = if ([Environment]::Is64BitProcess) { 'X64' } else { 'X86' }
try { $osArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() } catch {}
try { $processArch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() } catch {}

$result = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    os = [Environment]::OSVersion.VersionString
    osArchitecture = $osArch
    processArchitecture = $processArch
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
