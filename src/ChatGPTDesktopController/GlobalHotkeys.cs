using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ChatGPTDesktopController;

public sealed record HotkeyRegistration(int Id, string Name, bool Registered, int Win32Error);
public sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    private readonly HwndSource _source;
    private readonly Action<int> _action;
    private readonly List<HotkeyRegistration> _registrations = new();
    private readonly RightCtrlStateMachine _rightCtrl = new();
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly IntPtr _keyboardHook;
    public IReadOnlyList<HotkeyRegistration> Registrations => _registrations;
    public bool RightCtrlHookReady => _keyboardHook != IntPtr.Zero;
    public event Action<bool>? RightCtrlChanged;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    public GlobalHotkeys(Action<int> action)
    {
        _action = action;
        _source = new HwndSource(new HwndSourceParameters("CGPTControllerHotkeys") { Width = 0, Height = 0, WindowStyle = 0 }); _source.AddHook(WndProc);
        Register(1, "Ctrl+Alt+Q hide/show", MOD_CONTROL|MOD_ALT, 0x51); Register(2, "Ctrl+Alt+X click-through", MOD_CONTROL|MOD_ALT, 0x58);
        Register(3, "Ctrl+Alt+T topmost", MOD_CONTROL|MOD_ALT, 0x54); Register(4, "Ctrl+Alt+Up opacity +", MOD_CONTROL|MOD_ALT, 0x26);
        Register(5, "Ctrl+Alt+Down opacity -", MOD_CONTROL|MOD_ALT, 0x28); Register(6, "Ctrl+Alt+V paste image", MOD_CONTROL|MOD_ALT, 0x56);
        Register(7, "Ctrl+Alt+D diagnostics", MOD_CONTROL|MOD_ALT, 0x44); Register(8, "F6 capture active window", 0, 0x75);
        Register(9, "Ctrl+Shift+R voice", MOD_CONTROL|MOD_SHIFT, 0x52);
        _keyboardProc = KeyboardHook; _keyboardHook = SetWindowsHookEx(13, _keyboardProc, GetModuleHandle(null), 0);
    }
    private void Register(int id, string name, uint mods, uint key) { Marshal.SetLastPInvokeError(0); var ok = RegisterHotKey(_source.Handle, id, mods|MOD_NOREPEAT, key); _registrations.Add(new(id, name, ok, ok ? 0 : Marshal.GetLastWin32Error())); }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == WM_HOTKEY) { var id = wp.ToInt32(); ThreadPool.UnsafeQueueUserWorkItem(_ => _action(id), null); handled = true; } return IntPtr.Zero;
    }
    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0xA3)
        {
            if (message.ToInt32() == 0x0100 && _rightCtrl.OnDown()) QueueRightCtrl(true);
            else if (message.ToInt32() == 0x0101 && _rightCtrl.OnUp()) QueueRightCtrl(false);
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }
    private void QueueRightCtrl(bool down) => ThreadPool.UnsafeQueueUserWorkItem(_ => { try { RightCtrlChanged?.Invoke(down); } catch { } }, null);
    public void Dispose() { foreach (var h in _registrations.Where(x => x.Registered)) UnregisterHotKey(_source.Handle, h.Id); if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook); _source.Dispose(); }
}
