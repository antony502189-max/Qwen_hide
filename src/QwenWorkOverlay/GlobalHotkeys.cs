using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Interop;

namespace QwenWorkOverlay;

public sealed record HotkeyRegistration(int Id, string Name, bool Registered, int Win32Error);

public sealed class GlobalHotkeys : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Action<int> _action;
    private readonly AppLogger _log;
    private readonly RightCtrlStateMachine _rightCtrl = new();
    private readonly List<HotkeyRegistration> _registrations = new();
    private bool _voiceToggleActive;
    private long _lastHookDurationTicks;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WH_KEYBOARD_LL = 13;
    private const int VK_RCONTROL = 0xA3;
    private const int VoiceToggleHotkeyId = 12;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _hookProc;
    private readonly IntPtr _hook;

    public event Action<bool>? RightCtrlChanged;
    public event Action<bool>? VoiceToggleChanged;
    public IReadOnlyList<HotkeyRegistration> Registrations => _registrations;
    public bool HookReady => _hook != IntPtr.Zero;
    public TimeSpan LastHookDuration => TimeSpan.FromSeconds(Volatile.Read(ref _lastHookDurationTicks) / (double)Stopwatch.Frequency);
    public bool AllRegistered => _registrations.All(x => x.Registered) && HookReady;
    public string FailureSummary
    {
        get
        {
            var failed = _registrations.Where(x => !x.Registered).Select(x => $"{x.Name} (win32={x.Win32Error})").ToList();
            if (!HookReady) failed.Add("Right Ctrl hook");
            return failed.Count == 0 ? "All global hotkeys registered" : string.Join(", ", failed);
        }
    }

    public GlobalHotkeys(Action<int> action, AppLogger log)
    {
        _action = action;
        _log = log;
        _source = new HwndSource(new HwndSourceParameters("QDC_Hotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero,
            WindowStyle = 0
        });
        _hwnd = _source.Handle;
        _source.AddHook(WndProc);

        Register(1, "Ctrl+Alt+Q Hide/Show", MOD_CONTROL | MOD_ALT, 0x51);
        Register(2, "Ctrl+Alt+X Click-through", MOD_CONTROL | MOD_ALT, 0x58);
        Register(3, "Ctrl+Alt+T TopMost", MOD_CONTROL | MOD_ALT, 0x54);
        Register(5, "Ctrl+Alt+Up Opacity+", MOD_CONTROL | MOD_ALT, 0x26);
        Register(6, "Ctrl+Alt+Down Opacity-", MOD_CONTROL | MOD_ALT, 0x28);
        Register(7, "Ctrl+Alt+V Paste", MOD_CONTROL | MOD_ALT, 0x56);
        Register(8, "Ctrl+Alt+D Diagnostics", MOD_CONTROL | MOD_ALT, 0x44);
        Register(9, "F6 Screenshot", 0, 0x75);
        Register(10, "Shift+F6 Monitor screenshot", MOD_SHIFT, 0x75);
        Register(VoiceToggleHotkeyId, "Ctrl+Shift+R Qwen voice toggle", MOD_CONTROL | MOD_SHIFT, 0x52);

        _hookProc = KeyboardHook;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Error("Right Ctrl low-level keyboard hook registration failed; win32=" + Marshal.GetLastWin32Error());
        else
            _log.Info("Right Ctrl low-level keyboard hook registered");
    }

    private void Register(int id, string name, uint modifiers, uint key)
    {
        Marshal.SetLastPInvokeError(0);
        var ok = RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, key);
        var error = ok ? 0 : Marshal.GetLastWin32Error();
        _registrations.Add(new HotkeyRegistration(id, name, ok, error));
        if (!ok) _log.Error($"Global hotkey registration failed: {name}; win32={error}");
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == VoiceToggleHotkeyId)
            {
                _voiceToggleActive = !_voiceToggleActive;
                var active = _voiceToggleActive;
                QueueWork(() =>
                {
                    VoiceToggleChanged?.Invoke(active);
                    _log.Info("Ctrl+Shift+R Qwen voice toggle: " + (active ? "ON" : "OFF"));
                });
            }
            else
            {
                QueueWork(() =>
                {
                    _log.Info("Global hotkey queued: id=" + id);
                    _action(id);
                });
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        var started = Stopwatch.GetTimestamp();
        if (code >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == VK_RCONTROL)
            {
                if (wParam.ToInt32() == WM_KEYDOWN && _rightCtrl.OnDown()) QueueWork(() => RightCtrlChanged?.Invoke(true));
                else if (wParam.ToInt32() == WM_KEYUP && _rightCtrl.OnUp()) QueueWork(() => RightCtrlChanged?.Invoke(false));
            }
        }
        Interlocked.Exchange(ref _lastHookDurationTicks, Stopwatch.GetTimestamp() - started);
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void QueueWork(Action action) => ThreadPool.UnsafeQueueUserWorkItem(_ =>
    {
        try { action(); } catch { }
    }, null);

    public void Dispose()
    {
        foreach (var registration in _registrations.Where(x => x.Registered))
            UnregisterHotKey(_hwnd, registration.Id);
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
