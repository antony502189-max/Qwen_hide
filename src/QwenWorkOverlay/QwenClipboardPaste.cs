using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QwenWorkOverlay;

// Target-machine workaround for Qwen 1.0.3 clipboard paste.
//
// IMPORTANT: this file does not register, unregister, suppress, or replace any existing hotkey.
// GlobalHotkeys.cs remains untouched. We only observe the physical Ctrl+Alt+V chord with a cheap
// timer and, after the keys are released, reproduce the exact path that was manually validated on
// the target machine: activate Qwen -> real mouse click in composer -> real Ctrl+V -> restore mouse.
internal static class QwenClipboardPaste
{
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkV = 0x56;

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint KeyUp = 0x0002;

    private static Timer? _timer;
    private static int _chordSeen;
    private static int _pasteRunning;

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Polling avoids a second keyboard hook and therefore cannot disturb Ctrl+Alt+T/Q/X,
        // opacity hotkeys, Right Ctrl audio, or the existing RegisterHotKey dispatcher.
        _timer = new Timer(Poll, null, 500, 25);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { _timer?.Dispose(); } catch { }
        };
    }

    private static void Poll(object? _)
    {
        try
        {
            var chordDown = IsDown(VkControl) && IsDown(VkMenu) && IsDown(VkV);
            if (chordDown)
            {
                Interlocked.Exchange(ref _chordSeen, 1);
                return;
            }

            if (Interlocked.Exchange(ref _chordSeen, 0) == 1 &&
                Interlocked.CompareExchange(ref _pasteRunning, 1, 0) == 0)
            {
                _ = Task.Run(PasteAfterReleaseAsync);
            }
        }
        catch
        {
            // Observation must never affect the controller.
        }
    }

    private static async Task PasteAfterReleaseAsync()
    {
        try
        {
            // Let the original Ctrl+Alt+V handler finish its existing activation attempt first.
            await Task.Delay(180);

            var qwen = FindInstalledQwenWindow();
            if (qwen == IntPtr.Zero) return;

            Native.ShowWindowAsync(qwen, Native.SW_RESTORE);
            Native.ShowWindowAsync(qwen, Native.SW_SHOW);
            Native.SetForegroundWindow(qwen);
            await Task.Delay(160);

            if (!Native.GetWindowRect(qwen, out var rect)) return;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 320 || height < 240 || Native.IsMinimizedCoordinate(rect)) return;

            // If controller click-through is enabled, temporarily clear only WS_EX_TRANSPARENT so
            // the real click reaches Qwen. Restore the exact original extended style afterwards.
            var originalExStyle = Native.GetWindowLongPtr(qwen, Native.GWL_EXSTYLE);
            var hadClickThrough = (originalExStyle.ToInt64() & Native.WS_EX_TRANSPARENT) != 0;
            if (hadClickThrough)
            {
                var withoutTransparent = originalExStyle.ToInt64() & ~Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(qwen, Native.GWL_EXSTYLE, new IntPtr(withoutTransparent));
                Native.SetWindowPos(qwen, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                    Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                await Task.Delay(40);
            }

            try
            {
                if (!GetCursorPos(out var originalCursor)) return;

                // Qwen's composer occupies the lower central part of the native Chromium window.
                // Center X avoids attachment/mic/send buttons on the edges.
                var composerX = rect.Left + width / 2;
                var composerBottomOffset = Math.Clamp((int)Math.Round(height * 0.095), 72, 108);
                var composerY = rect.Bottom - composerBottomOffset;

                try
                {
                    if (!SetCursorPos(composerX, composerY)) return;
                    await Task.Delay(45);
                    if (!SendMouseClick()) return;
                    await Task.Delay(140);
                }
                finally
                {
                    SetCursorPos(originalCursor.X, originalCursor.Y);
                }

                // The trigger chord is already released here, so this is a clean Ctrl+V sequence.
                SendCtrlV();
            }
            finally
            {
                if (hadClickThrough && Native.IsWindow(qwen))
                {
                    Native.SetWindowLongPtr(qwen, Native.GWL_EXSTYLE, originalExStyle);
                    Native.SetWindowPos(qwen, IntPtr.Zero, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                        Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                }
            }
        }
        catch
        {
            // The old manual Ctrl+V path remains usable even if automation fails.
        }
        finally
        {
            Interlocked.Exchange(ref _pasteRunning, 0);
        }
    }

    private static IntPtr FindInstalledQwenWindow()
    {
        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Qwen", "Qwen.exe");

        IntPtr best = IntPtr.Zero;
        long bestArea = -1;

        foreach (var process in Process.GetProcessesByName("Qwen"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path) ||
                    !string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var hwnd in Native.EnumerateTopLevelWindows((uint)process.Id))
                {
                    if (!Native.IsWindow(hwnd)) continue;
                    var area = Native.GetWindowArea(hwnd);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = hwnd;
                    }
                }
            }
            catch
            {
                // Process may exit while enumerated.
            }
            finally
            {
                process.Dispose();
            }
        }

        return best;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool SendMouseClick()
    {
        var inputs = new[]
        {
            Mouse(MouseLeftDown),
            Mouse(MouseLeftUp)
        };
        Marshal.SetLastPInvokeError(0);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static bool SendCtrlV()
    {
        var inputs = new[]
        {
            Key(VkControl, false),
            Key(VkV, false),
            Key(VkV, true),
            Key(VkControl, true)
        };
        Marshal.SetLastPInvokeError(0);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static Input Mouse(uint flags) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new MouseInput { Flags = flags }
        }
    };

    private static Input Key(int vk, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = (ushort)vk,
                Flags = keyUp ? KeyUp : 0
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
