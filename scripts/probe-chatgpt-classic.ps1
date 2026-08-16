[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -ReferencedAssemblies 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Accessibility.dll' -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text; using Accessibility;
public static class CgptProbeNative {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object obj);
 public static string MsaaSummary(IntPtr hwnd) {
   try { Guid iid=typeof(IAccessible).GUID; object obj; int hr=AccessibleObjectFromWindow(hwnd,0xFFFFFFFC,ref iid,out obj); if(hr!=0) return "MSAA unavailable HRESULT=0x"+hr.ToString("X8"); var accessible=(IAccessible)obj; var text="MSAA root name="+(accessible.get_accName(0)??"<none>")+"; children="+accessible.accChildCount; for(int i=1;i<=Math.Min(accessible.accChildCount,20);i++){ try { var n=accessible.get_accName(i); var r=accessible.get_accRole(i); text += "\nMSAA child "+i+": role="+r+" name="+(n??"<none>"); } catch{} } return text; } catch(Exception ex) { return "MSAA probe failed: "+ex.GetType().Name; }
 }
}
'@
$targets = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'ChatGPT Classic.exe' -and $_.ExecutablePath -match '\\WindowsApps\\OpenAI\.ChatGPT-Desktop_' }
if (!$targets) { Write-Output 'ChatGPT Classic is not running.'; exit 1 }
foreach ($target in $targets) {
  $hwnds = [System.Collections.Generic.List[IntPtr]]::new(); $callback = [CgptProbeNative+EnumProc]{ param($h,$l) $windowProcessId=0; [CgptProbeNative]::GetWindowThreadProcessId($h,[ref]$windowProcessId)|Out-Null; if($windowProcessId -eq $target.ProcessId -and [CgptProbeNative]::IsWindowVisible($h)){$hwnds.Add($h)}; return $true }; [CgptProbeNative]::EnumWindows($callback,[IntPtr]::Zero)|Out-Null
  foreach($hwnd in $hwnds) {
    $class=[Text.StringBuilder]::new(256);$title=[Text.StringBuilder]::new(1024);[CgptProbeNative]::GetClassName($hwnd,$class,$class.Capacity)|Out-Null;[CgptProbeNative]::GetWindowText($hwnd,$title,$title.Capacity)|Out-Null
    [PSCustomObject]@{Pid=$target.ProcessId;Executable=$target.ExecutablePath;Hwnd=('0x{0:X}' -f $hwnd.ToInt64());Class=$class.ToString();Title=$title.ToString()}
    [CgptProbeNative]::MsaaSummary($hwnd)
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $e=$_; $c=$e.Current; if($c.ControlType.ProgrammaticName -in 'ControlType.Edit','ControlType.Document','ControlType.Button'){ [PSCustomObject]@{Control=$c.ControlType.ProgrammaticName;Name=$c.Name;AutomationId=$c.AutomationId;Focusable=$c.IsKeyboardFocusable;Enabled=$c.IsEnabled;Help=$c.HelpText} } }
  }
}
