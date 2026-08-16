using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ChatGPTDesktopController;

// A dedicated native message window keeps emergency recovery outside the normal window's dispatcher work queue.
public sealed class EmergencyHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x312;
    private readonly HwndSource _source;
    private readonly Action _action;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public bool Registered { get; }
    public int Win32Error { get; }
    public EmergencyHotkey(Action action)
    {
        _action = action; _source = new HwndSource(new HwndSourceParameters("CGPTControllerEmergency") { Width = 0, Height = 0, WindowStyle = 0 }); _source.AddHook(WndProc);
        Marshal.SetLastPInvokeError(0); Registered = RegisterHotKey(_source.Handle, 99, 1|2|0x4000, 0x1B); Win32Error = Registered ? 0 : Marshal.GetLastWin32Error();
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) { if (message == WM_HOTKEY) { ThreadPool.UnsafeQueueUserWorkItem(_ => _action(), null); handled = true; } return IntPtr.Zero; }
    public void Dispose() { if (Registered) UnregisterHotKey(_source.Handle, 99); _source.Dispose(); }
}
