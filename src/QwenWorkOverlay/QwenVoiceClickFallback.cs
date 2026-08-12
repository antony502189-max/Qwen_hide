using System.Runtime.InteropServices;
using System.Text.Json;

namespace QwenWorkOverlay;

public sealed record VoiceClickCalibration(double OffsetFromRight, double OffsetFromBottom, string? WindowClass, DateTimeOffset UpdatedAt);

public sealed class QwenVoiceClickFallback
{
    private readonly AppLogger _log;
    public static string CalibrationPath => Path.Combine(SettingsService.Root, "voice-calibration.json");

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public QwenVoiceClickFallback(AppLogger log) => _log = log;

    public bool HasCalibration => TryLoad(out _);

    public string Status
    {
        get
        {
            if (!TryLoad(out var calibration)) return "not calibrated";
            return $"calibrated {calibration.OffsetFromRight:0.#}px from right / {calibration.OffsetFromBottom:0.#}px from bottom";
        }
    }

    public bool TryInvoke(IntPtr qwenHwnd, out string diagnostic)
    {
        diagnostic = "calibrated click unavailable";
        if (qwenHwnd == IntPtr.Zero || !Native.IsWindow(qwenHwnd))
        {
            diagnostic = "Qwen window unavailable";
            return false;
        }

        if (!TryLoad(out var calibration))
        {
            diagnostic = "voice click fallback is not calibrated";
            return false;
        }

        if (!GetClientRect(qwenHwnd, out var client))
        {
            diagnostic = "GetClientRect failed";
            return false;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        var point = ComputeClientPoint(width, height, calibration.OffsetFromRight, calibration.OffsetFromBottom);
        if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
        {
            diagnostic = "saved voice calibration falls outside the current Qwen client area";
            return false;
        }

        var screenPoint = new POINT { X = point.X, Y = point.Y };
        if (!ClientToScreen(qwenHwnd, ref screenPoint))
        {
            diagnostic = "ClientToScreen failed";
            return false;
        }

        var clickHwnd = WindowFromPoint(screenPoint);
        if (clickHwnd == IntPtr.Zero) clickHwnd = qwenHwnd;
        var root = GetAncestor(clickHwnd, GA_ROOT);
        if (root != qwenHwnd)
        {
            diagnostic = "calibrated point no longer belongs to the Qwen window";
            return false;
        }

        var childPoint = screenPoint;
        if (!ScreenToClient(clickHwnd, ref childPoint))
        {
            diagnostic = "ScreenToClient failed";
            return false;
        }

        var lParam = PackPoint(childPoint.X, childPoint.Y);
        var moved = PostMessage(clickHwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
        var down = PostMessage(clickHwnd, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), lParam);
        var up = PostMessage(clickHwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
        if (!down || !up)
        {
            diagnostic = $"PostMessage click failed (move={moved}, down={down}, up={up})";
            return false;
        }

        diagnostic = $"calibrated click posted to HWND 0x{clickHwnd.ToInt64():X}";
        _log.Info("Qwen voice calibrated click fallback invoked: " + diagnostic);
        return true;
    }

    public static (int X, int Y) ComputeClientPoint(int width, int height, double offsetFromRight, double offsetFromBottom)
    {
        var x = (int)Math.Round(width - offsetFromRight, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(height - offsetFromBottom, MidpointRounding.AwayFromZero);
        return (x, y);
    }

    private bool TryLoad(out VoiceClickCalibration calibration)
    {
        calibration = new VoiceClickCalibration(0, 0, null, DateTimeOffset.MinValue);
        try
        {
            if (!File.Exists(CalibrationPath)) return false;
            var parsed = JsonSerializer.Deserialize<VoiceClickCalibration>(File.ReadAllText(CalibrationPath));
            if (parsed is null || !double.IsFinite(parsed.OffsetFromRight) || !double.IsFinite(parsed.OffsetFromBottom) ||
                parsed.OffsetFromRight <= 0 || parsed.OffsetFromBottom <= 0)
                return false;
            calibration = parsed;
            return true;
        }
        catch (Exception ex)
        {
            _log.Info("Voice click calibration load failed: " + ex.GetType().Name);
            return false;
        }
    }

    private static IntPtr PackPoint(int x, int y)
    {
        var packed = ((y & 0xFFFF) << 16) | (x & 0xFFFF);
        return new IntPtr(packed);
    }
}
