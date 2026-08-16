using System.Runtime.InteropServices;

namespace QwenWorkOverlay;

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

    public static async Task<bool> PasteAsync(QwenWindowController qwen, AppLogger log)
    {
        if (!qwen.IsAttached || qwen.Target is null) return false;

        // WM_HOTKEY is delivered while Ctrl/Alt/V can still be physically down.
        // Wait here, inside paste only, so the stable global hotkey dispatcher stays untouched.
        if (!await WaitForTriggerReleaseAsync())
        {
            log.Error("Ctrl+Alt+V paste cancelled: trigger keys did not release");
            return false;
        }

        var restoreClickThrough = qwen.ClickThrough;
        if (restoreClickThrough && !qwen.SetClickThrough(false))
        {
            log.Error("Ctrl+Alt+V paste could not temporarily disable click-through");
            return false;
        }

        try
        {
            if (!qwen.ShowAndActivate()) return false;
            await Task.Delay(180);

            var hwnd = qwen.Target?.Hwnd ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.GetWindowRect(hwnd, out var rect))
                return false;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 320 || height < 240 || Native.IsMinimizedCoordinate(rect)) return false;

            // Manual validation on this machine proved that a real mouse click in the Qwen
            // composer followed by Ctrl+V pastes the F6 screenshot correctly. Reproduce exactly
            // that input path. The cursor is restored immediately after the click.
            var x = rect.Left + width / 2;
            var bottomOffset = Math.Clamp((int)Math.Round(height * 0.095), 72, 108);
            var y = rect.Bottom - bottomOffset;

            if (!GetCursorPos(out var originalCursor)) return false;
            try
            {
                if (!SetCursorPos(x, y)) return false;
                await Task.Delay(35);
                if (!SendMouseClick()) return false;
                await Task.Delay(120);
            }
            finally
            {
                SetCursorPos(originalCursor.X, originalCursor.Y);
            }

            // A real click gives Chromium's composer the same focus as the user's successful
            // manual test. Send a clean Ctrl+V only after the trigger modifiers are released.
            if (IsDown(VkControl) || IsDown(VkMenu) || IsDown(VkV))
            {
                if (!await WaitForTriggerReleaseAsync()) return false;
            }

            var pasted = SendCtrlV();
            if (!pasted) log.Error("Ctrl+Alt+V SendInput Ctrl+V failed; win32=" + Marshal.GetLastWin32Error());
            return pasted;
        }
        catch (Exception ex)
        {
            log.Error("Ctrl+Alt+V native paste failed: " + ex.GetType().Name);
            return false;
        }
        finally
        {
            if (restoreClickThrough) qwen.SetClickThrough(true);
        }
    }

    private static async Task<bool> WaitForTriggerReleaseAsync()
    {
        for (var i = 0; i < 150; i++)
        {
            if (!IsDown(VkControl) && !IsDown(VkMenu) && !IsDown(VkV)) return true;
            await Task.Delay(10);
        }
        return !IsDown(VkControl) && !IsDown(VkMenu) && !IsDown(VkV);
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
