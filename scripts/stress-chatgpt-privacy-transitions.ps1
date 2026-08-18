[CmdletBinding()]
param(
    [int]$Cycles = 25,
    [switch]$IncludeF6,
    [int]$PollMilliseconds = 10
)

$ErrorActionPreference = 'Stop'
if ($Cycles -lt 1 -or $Cycles -gt 200) { throw 'Cycles must be between 1 and 200.' }
if ($PollMilliseconds -lt 5 -or $PollMilliseconds -gt 100) { throw 'PollMilliseconds must be between 5 and 100.' }

if (-not ('ChatGPTPrivacyStress.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ChatGPTPrivacyStress
{
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }
}
'@
}

$VK_CONTROL=0x11; $VK_MENU=0x12; $VK_Q=0x51; $VK_X=0x58; $VK_T=0x54; $VK_UP=0x26; $VK_DOWN=0x28; $VK_F6=0x75; $KEYUP=0x2

function Send-KeyCombo([byte[]]$Modifiers,[byte]$Key) {
    foreach($m in $Modifiers){ [ChatGPTPrivacyStress.Native]::keybd_event($m,0,0,[UIntPtr]::Zero) }
    [ChatGPTPrivacyStress.Native]::keybd_event($Key,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 15
    [ChatGPTPrivacyStress.Native]::keybd_event($Key,0,$KEYUP,[UIntPtr]::Zero)
    for($i=$Modifiers.Length-1;$i-ge 0;$i--){ [ChatGPTPrivacyStress.Native]::keybd_event($Modifiers[$i],0,$KEYUP,[UIntPtr]::Zero) }
}

function Find-Target {
    $pids=[Collections.Generic.HashSet[uint32]]::new()
    Get-Process -Name 'ChatGPT Classic' -ErrorAction SilentlyContinue | ForEach-Object { [void]$pids.Add([uint32]$_.Id) }
    if($pids.Count -eq 0){ throw 'ChatGPT Classic is not running.' }
    $items=[Collections.Generic.List[object]]::new()
    $cb=[ChatGPTPrivacyStress.Native+EnumWindowsProc]{ param([IntPtr]$hwnd,[IntPtr]$lParam)
        [uint32]$ownerPid=0; [void][ChatGPTPrivacyStress.Native]::GetWindowThreadProcessId($hwnd,[ref]$ownerPid)
        if(-not $pids.Contains($ownerPid) -or -not [ChatGPTPrivacyStress.Native]::IsWindowVisible($hwnd)){ return $true }
        $rect=New-Object ChatGPTPrivacyStress.Native+RECT
        if(-not [ChatGPTPrivacyStress.Native]::GetWindowRect($hwnd,[ref]$rect)){ return $true }
        $w=$rect.Right-$rect.Left; $h=$rect.Bottom-$rect.Top
        if($w -ge 320 -and $h -ge 200){ $items.Add([pscustomobject]@{Hwnd=$hwnd;Pid=$ownerPid;Area=([int64]$w*$h)}) }
        return $true
    }
    [void][ChatGPTPrivacyStress.Native]::EnumWindows($cb,[IntPtr]::Zero)
    $target=$items|Sort-Object Area -Descending|Select-Object -First 1
    if(-not $target){ throw 'No ChatGPT Classic main HWND found.' }
    return $target
}

function Read-Protection([IntPtr]$Hwnd) {
    [uint32]$a=0; $ok=[ChatGPTPrivacyStress.Native]::GetWindowDisplayAffinity($Hwnd,[ref]$a)
    [pscustomobject]@{Ok=$ok;Affinity=$a;Verified=($ok -and $a -eq 0x11)}
}

function Wait-Visibility([IntPtr]$Hwnd,[bool]$Visible,[int]$TimeoutMs=2000) {
    $sw=[Diagnostics.Stopwatch]::StartNew()
    while($sw.ElapsedMilliseconds -lt $TimeoutMs){
        if(-not [ChatGPTPrivacyStress.Native]::IsWindow($Hwnd)){ return $false }
        if([ChatGPTPrivacyStress.Native]::IsWindowVisible($Hwnd) -eq $Visible){ return $true }
        Start-Sleep -Milliseconds $PollMilliseconds
    }
    return $false
}

function Measure-VisibleProtection([IntPtr]$Hwnd,[int]$TimeoutMs=1500) {
    $sw=[Diagnostics.Stopwatch]::StartNew(); $firstVisible=$null; $firstVerified=$null; $unverifiedSamples=0
    while($sw.ElapsedMilliseconds -lt $TimeoutMs){
        if(-not [ChatGPTPrivacyStress.Native]::IsWindow($Hwnd)){ return [pscustomobject]@{Result='WINDOW_GONE';GapMs=$null;Unverified=$unverifiedSamples} }
        $visible=[ChatGPTPrivacyStress.Native]::IsWindowVisible($Hwnd)
        if($visible -and $null -eq $firstVisible){ $firstVisible=$sw.Elapsed.TotalMilliseconds }
        if($visible){
            $p=Read-Protection $Hwnd
            if($p.Verified -and $null -eq $firstVerified){ $firstVerified=$sw.Elapsed.TotalMilliseconds; break }
            if(-not $p.Verified){ $unverifiedSamples++ }
        } elseif($null -ne $firstVisible){
            return [pscustomobject]@{Result='FAIL_CLOSED_HIDDEN';GapMs=($sw.Elapsed.TotalMilliseconds-$firstVisible);Unverified=$unverifiedSamples}
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }
    if($null -eq $firstVisible){ return [pscustomobject]@{Result='NEVER_VISIBLE';GapMs=$null;Unverified=$unverifiedSamples} }
    if($null -eq $firstVerified){ return [pscustomobject]@{Result='VISIBLE_UNVERIFIED_TIMEOUT';GapMs=$TimeoutMs;Unverified=$unverifiedSamples} }
    return [pscustomobject]@{Result='VERIFIED';GapMs=[Math]::Max(0,$firstVerified-$firstVisible);Unverified=$unverifiedSamples}
}

function Test-TogglePair([IntPtr]$Hwnd,[byte]$Key,[string]$Name,[int]$Cycle) {
    $toggled=$false
    try {
        Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) $Key
        $toggled=$true
        Start-Sleep -Milliseconds 40
        if(-not (Read-Protection $Hwnd).Verified){ return "$Name[$Cycle] lost affinity" }
        return $null
    }
    finally {
        if($toggled -and [ChatGPTPrivacyStress.Native]::IsWindow($Hwnd) -and [ChatGPTPrivacyStress.Native]::IsWindowVisible($Hwnd)){
            Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) $Key
            Start-Sleep -Milliseconds 40
        }
    }
}

function Test-OpacityPair([IntPtr]$Hwnd,[int]$Cycle) {
    $downApplied=$false
    try {
        Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) ([byte]$VK_DOWN)
        $downApplied=$true
        Start-Sleep -Milliseconds 40
        if(-not (Read-Protection $Hwnd).Verified){ return "OpacityDown[$Cycle] lost affinity" }
        return $null
    }
    finally {
        if($downApplied -and [ChatGPTPrivacyStress.Native]::IsWindow($Hwnd) -and [ChatGPTPrivacyStress.Native]::IsWindowVisible($Hwnd)){
            Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) ([byte]$VK_UP)
            Start-Sleep -Milliseconds 40
        }
    }
}

$target=Find-Target
if(-not [ChatGPTPrivacyStress.Native]::IsWindowVisible($target.Hwnd)){ throw 'Precondition: ChatGPT main window must be visible.' }
$initial=Read-Protection $target.Hwnd
if(-not $initial.Verified){ throw ('Precondition: visible target affinity is not 0x11; got 0x{0:X}' -f $initial.Affinity) }

Write-Host ('TARGET HWND=0x{0:X} PID={1} Cycles={2} Poll={3}ms' -f $target.Hwnd.ToInt64(),$target.Pid,$Cycles,$PollMilliseconds)
$maxGap=0.0; $failures=[Collections.Generic.List[string]]::new(); $failClosed=0

for($cycle=1;$cycle -le $Cycles;$cycle++){
    Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) ([byte]$VK_Q)
    if(-not (Wait-Visibility $target.Hwnd $false)){ $failures.Add("Q[$cycle] did not hide"); break }
    Send-KeyCombo @([byte]$VK_CONTROL,[byte]$VK_MENU) ([byte]$VK_Q)
    $m=Measure-VisibleProtection $target.Hwnd
    if($m.GapMs -ne $null -and $m.GapMs -gt $maxGap){ $maxGap=$m.GapMs }
    if($m.Result -eq 'FAIL_CLOSED_HIDDEN'){ $failClosed++; $failures.Add("Q[$cycle] fail-closed hid target after show"); break }
    if($m.Result -ne 'VERIFIED'){ $failures.Add("Q[$cycle] $($m.Result)"); break }

    $toggleError=Test-TogglePair $target.Hwnd ([byte]$VK_T) 'T' $cycle
    if($toggleError){ $failures.Add($toggleError); break }

    $toggleError=Test-TogglePair $target.Hwnd ([byte]$VK_X) 'X' $cycle
    if($toggleError){ $failures.Add($toggleError); break }

    $toggleError=Test-OpacityPair $target.Hwnd $cycle
    if($toggleError){ $failures.Add($toggleError); break }

    if($IncludeF6){
        Send-KeyCombo @() ([byte]$VK_F6)
        $f6=Measure-VisibleProtection $target.Hwnd 2500
        if($f6.GapMs -ne $null -and $f6.GapMs -gt $maxGap){ $maxGap=$f6.GapMs }
        if($f6.Result -ne 'VERIFIED'){ $failures.Add("F6[$cycle] $($f6.Result)"); break }
    }

    Write-Host ('CYCLE {0}/{1} PASS maxVisibleUnverifiedMs={2:F1}' -f $cycle,$Cycles,$maxGap)
}

$finalVisible=[ChatGPTPrivacyStress.Native]::IsWindowVisible($target.Hwnd)
$final=Read-Protection $target.Hwnd
Write-Host ('SUMMARY failures={0} failClosed={1} maxVisibleUnverifiedMs={2:F1} finalVisible={3} finalAffinity=0x{4:X}' -f $failures.Count,$failClosed,$maxGap,$finalVisible,$final.Affinity)
foreach($failure in $failures){ Write-Host "FAILURE $failure" }

# On a privacy failure, intentionally do not force-show a target that fail-closed logic hid.
if($failures.Count -gt 0 -or ($finalVisible -and -not $final.Verified)){ exit 2 }
