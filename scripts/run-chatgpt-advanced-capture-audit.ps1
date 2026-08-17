[CmdletBinding()]
param(
    [switch]$BuildProbes,
    [int]$RecoveryTimeoutMilliseconds = 2500
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'tools\ChatGPTPrivacyCaptureProbe\bin'
$dxgi = Join-Path $bin 'chatgpt-dxgi-capture-probe.exe'
$wgc = Join-Path $bin 'chatgpt-wgc-capture-probe.exe'

if (-not ('ChatGPTAdvancedAudit.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ChatGPTAdvancedAudit
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
        [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    }
}
'@
}

function Find-Target {
    $pids = [System.Collections.Generic.HashSet[uint32]]::new()
    Get-Process -Name 'ChatGPT Classic' -ErrorAction SilentlyContinue | ForEach-Object { [void]$pids.Add([uint32]$_.Id) }
    if ($pids.Count -eq 0) { throw 'ChatGPT Classic is not running.' }
    $items = [System.Collections.Generic.List[object]]::new()
    $callback = [ChatGPTAdvancedAudit.Native+EnumWindowsProc]{
        param([IntPtr]$hwnd, [IntPtr]$lParam)
        [uint32]$ownerPid = 0
        [void][ChatGPTAdvancedAudit.Native]::GetWindowThreadProcessId($hwnd, [ref]$ownerPid)
        if (-not $pids.Contains($ownerPid) -or -not [ChatGPTAdvancedAudit.Native]::IsWindowVisible($hwnd)) { return $true }
        $rect = New-Object ChatGPTAdvancedAudit.Native+RECT
        if (-not [ChatGPTAdvancedAudit.Native]::GetWindowRect($hwnd, [ref]$rect)) { return $true }
        $w=$rect.Right-$rect.Left; $h=$rect.Bottom-$rect.Top
        if ($w -ge 320 -and $h -ge 200) { $items.Add([pscustomobject]@{Hwnd=$hwnd;Pid=$ownerPid;Area=([int64]$w*$h)}) }
        return $true
    }
    [void][ChatGPTAdvancedAudit.Native]::EnumWindows($callback,[IntPtr]::Zero)
    $target=$items|Sort-Object Area -Descending|Select-Object -First 1
    if (-not $target) { throw 'No visible ChatGPT Classic main window found.' }
    return $target
}

function Read-Affinity([IntPtr]$Hwnd) {
    [uint32]$affinity=0
    $ok=[ChatGPTAdvancedAudit.Native]::GetWindowDisplayAffinity($Hwnd,[ref]$affinity)
    [pscustomobject]@{ Ok=$ok; Affinity=$affinity; Error=($(if($ok){0}else{[Runtime.InteropServices.Marshal]::GetLastWin32Error()})) }
}

function Wait-Protected([IntPtr]$Hwnd,[string]$Context) {
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while($sw.ElapsedMilliseconds -lt $RecoveryTimeoutMilliseconds) {
        if(-not [ChatGPTAdvancedAudit.Native]::IsWindow($Hwnd)) { Write-Host "RECOVERY $Context WINDOW_GONE"; return $false }
        $state=Read-Affinity $Hwnd
        if($state.Ok -and $state.Affinity -eq 0x11) { Write-Host ("RECOVERY {0} PASS {1}ms" -f $Context,$sw.ElapsedMilliseconds); return $true }
        Start-Sleep -Milliseconds 25
    }
    $last=Read-Affinity $Hwnd
    Write-Host ("RECOVERY {0} FAIL {1}ms affinity={2} getter={3} error={4}" -f $Context,$sw.ElapsedMilliseconds,('0x{0:X}' -f $last.Affinity),$last.Ok,$last.Error)
    return $false
}

if($BuildProbes -or -not (Test-Path -LiteralPath $dxgi)) {
    & (Join-Path $PSScriptRoot 'build-chatgpt-advanced-capture-probes.ps1')
}
if(-not (Test-Path -LiteralPath $dxgi)) { throw 'DXGI probe is unavailable.' }

$target=Find-Target
$initial=Read-Affinity $target.Hwnd
Write-Host ('TARGET HWND=0x{0:X} PID={1} affinity={2} getter={3} error={4}' -f $target.Hwnd.ToInt64(),$target.Pid,('0x{0:X}' -f $initial.Affinity),$initial.Ok,$initial.Error)
if(-not $initial.Ok -or $initial.Affinity -ne 0x11) { throw 'Precondition failed: visible ChatGPT is not externally verified at WDA_EXCLUDEFROMCAPTURE (0x11).' }

$hwndText=('0x{0:X}' -f $target.Hwnd.ToInt64())
Write-Host '--- DXGI Desktop Duplication ---'
& $dxgi $hwndText
$dxgiExit=$LASTEXITCODE
$dxgiRecovery=Wait-Protected $target.Hwnd 'after-DXGI-hide-show'

$wgcAvailable=Test-Path -LiteralPath $wgc
if($wgcAvailable) {
    Write-Host '--- Windows Graphics Capture monitor ---'
    & $wgc monitor $hwndText
    $wgcMonitorExit=$LASTEXITCODE
    $wgcMonitorRecovery=Wait-Protected $target.Hwnd 'after-WGC-monitor-hide-show'

    Write-Host '--- Windows Graphics Capture window ---'
    & $wgc window $hwndText
    $wgcWindowExit=$LASTEXITCODE
    $wgcWindowRecovery=Wait-Protected $target.Hwnd 'after-WGC-window'
} else {
    Write-Host 'RESULT WGC=UNSUPPORTED Detail=probe-not-built'
    $wgcMonitorExit=$null; $wgcWindowExit=$null; $wgcMonitorRecovery=$null; $wgcWindowRecovery=$null
}

$final=Read-Affinity $target.Hwnd
Write-Host ('FINAL affinity={0} getter={1} error={2}' -f ('0x{0:X}' -f $final.Affinity),$final.Ok,$final.Error)
Write-Host ('SUMMARY DXGIExit={0} DXGIRecovery={1} WGCMonitorExit={2} WGCMonitorRecovery={3} WGCWindowExit={4} WGCWindowRecovery={5}' -f $dxgiExit,$dxgiRecovery,$wgcMonitorExit,$wgcMonitorRecovery,$wgcWindowExit,$wgcWindowRecovery)

if(-not $final.Ok -or $final.Affinity -ne 0x11 -or -not $dxgiRecovery -or ($wgcAvailable -and (-not $wgcMonitorRecovery -or -not $wgcWindowRecovery))) { exit 2 }
