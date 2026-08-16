[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class CgptProbeNative {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
$targets = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'ChatGPT Classic.exe' -and $_.ExecutablePath -match '\\WindowsApps\\OpenAI\.ChatGPT-Desktop_' }
if (!$targets) { Write-Output 'ChatGPT Classic is not running.'; exit 1 }
foreach ($target in $targets) {
  $hwnds = [System.Collections.Generic.List[IntPtr]]::new(); $callback = [CgptProbeNative+EnumProc]{ param($h,$l) $windowProcessId=0; [CgptProbeNative]::GetWindowThreadProcessId($h,[ref]$windowProcessId)|Out-Null; if($windowProcessId -eq $target.ProcessId -and [CgptProbeNative]::IsWindowVisible($h)){$hwnds.Add($h)}; return $true }; [CgptProbeNative]::EnumWindows($callback,[IntPtr]::Zero)|Out-Null
  foreach($hwnd in $hwnds) {
    $class=[Text.StringBuilder]::new(256);$title=[Text.StringBuilder]::new(1024);[CgptProbeNative]::GetClassName($hwnd,$class,$class.Capacity)|Out-Null;[CgptProbeNative]::GetWindowText($hwnd,$title,$title.Capacity)|Out-Null
    [PSCustomObject]@{Pid=$target.ProcessId;Executable=$target.ExecutablePath;Hwnd=('0x{0:X}' -f $hwnd.ToInt64());Class=$class.ToString();Title=$title.ToString()}
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $e=$_; $c=$e.Current; if($c.ControlType.ProgrammaticName -in 'ControlType.Edit','ControlType.Document','ControlType.Button'){ [PSCustomObject]@{Control=$c.ControlType.ProgrammaticName;Name=$c.Name;AutomationId=$c.AutomationId;Focusable=$c.IsKeyboardFocusable;Enabled=$c.IsEnabled;Help=$c.HelpText} } }
  }
}
