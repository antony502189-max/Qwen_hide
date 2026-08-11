using System.Runtime.InteropServices;

namespace QwenWorkOverlay;
internal static class Native
{
    public const int GWL_EXSTYLE=-20, WS_EX_TRANSPARENT=0x20, WS_EX_LAYERED=0x80000, LWA_ALPHA=2;
    public const uint WDA_NONE=0, WDA_EXCLUDEFROMCAPTURE=0x11;
    public const int WM_NCHITTEST=0x84, HTCLIENT=1, HTLEFT=10, HTRIGHT=11, HTTOP=12, HTTOPLEFT=13, HTTOPRIGHT=14, HTBOTTOM=15, HTBOTTOMLEFT=16, HTBOTTOMRIGHT=17;
    [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowLong(IntPtr hWnd,int nIndex);
    [DllImport("user32.dll", SetLastError=true)] public static extern int SetWindowLong(IntPtr hWnd,int nIndex,int value);
    [DllImport("user32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint crKey,byte alpha,uint flags);
    [DllImport("user32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd,uint affinity);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    public const byte VK_CONTROL=0x11, VK_V=0x56; public const uint KEYEVENTF_KEYUP=2;
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
}
public sealed class CaptureProtectionService
{
    public bool Active { get; private set; }
    public CaptureProtectionResult LastResult { get; private set; }
    public bool Set(IntPtr hwnd,bool enabled) { var success = Native.SetWindowDisplayAffinity(hwnd, enabled ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE); LastResult=new(enabled,success); Active = LastResult.IsProtected; return Active; }
}
