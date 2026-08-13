using System.Runtime.InteropServices;

namespace QwenWorkOverlay;

// Runs separately from WPF's Dispatcher. A blocked controller UI must not prevent recovery.
public sealed class EmergencyHotkey : IDisposable
{
    private const int Id = 91;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkEscape = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Value;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint minimum, uint maximum);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Action _recover;
    private readonly AppLogger _log;
    private readonly Thread _thread;
    private int _threadId;
    private int _registered;
    private int _error;
    private int _disposed;

    public bool Registered => Volatile.Read(ref _registered) != 0;
    public string Status => Registered
        ? "READY (dedicated recovery thread)"
        : Volatile.Read(ref _error) != 0 ? "FAILED (win32=" + Volatile.Read(ref _error) + ")" : "initializing";

    public EmergencyHotkey(Action recover, AppLogger log)
    {
        _recover = recover;
        _log = log;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "QDC.EmergencyHotkey" };
        _thread.Start();
    }

    private void MessageLoop()
    {
        Volatile.Write(ref _threadId, unchecked((int)GetCurrentThreadId()));
        if (Volatile.Read(ref _disposed) != 0) return;
        Marshal.SetLastPInvokeError(0);
        if (!RegisterHotKey(IntPtr.Zero, Id, ModAlt | ModControl | ModNoRepeat, VkEscape))
        {
            var error = Marshal.GetLastWin32Error();
            Volatile.Write(ref _error, error);
            _log.Error("Dedicated emergency hotkey registration failed; win32=" + error);
            return;
        }

        Volatile.Write(ref _registered, 1);
        _log.Info("Dedicated Ctrl+Alt+Esc emergency hotkey registered");
        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Value != WmHotkey || message.WParam.ToUInt64() != Id) continue;
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try { _recover(); } catch { }
                }, null);
            }
        }
        finally
        {
            if (Registered) UnregisterHotKey(IntPtr.Zero, Id);
            Volatile.Write(ref _registered, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var threadId = unchecked((uint)Volatile.Read(ref _threadId));
        if (threadId != 0) PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        try { _thread.Join(TimeSpan.FromSeconds(1)); } catch { }
    }
}
