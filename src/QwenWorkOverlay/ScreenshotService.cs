using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QwenWorkOverlay;

public static class ScreenshotService
{
    public static bool CaptureWindowToClipboard(IntPtr hwnd, IntPtr qwenHwnd = default)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.GetWindowRect(hwnd, out var rect)) return false;
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        try
        {
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var printed = false;
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var hdc = graphics.GetHdc();
                try { printed = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT); }
                finally { graphics.ReleaseHdc(hdc); }
            }

            // Electron/Chromium windows can report PrintWindow success while returning a black surface.
            if (!printed || LooksBlank(bitmap))
            {
                var presentation = QwenPresentationSnapshot.Capture(qwenHwnd);
                try
                {
                    presentation.HideForCapture();
                    if (presentation.WasVisible) Thread.Sleep(65);
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }
                finally
                {
                    presentation.Restore();
                }
            }

            return CopyBitmapToClipboard(bitmap);
        }
        catch
        {
            return false;
        }
    }

    public static bool CaptureMonitorToClipboard(IntPtr qwenHwnd)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var bounds = System.Windows.Forms.Screen.FromPoint(cursor).Bounds;
        var presentation = QwenPresentationSnapshot.Capture(qwenHwnd);
        try
        {
            // The native Qwen window cannot safely use WDA_EXCLUDEFROMCAPTURE from this companion process.
            // For screenshots created by this helper only, hide Qwen briefly and restore the exact visible/minimized/maximized state.
            presentation.HideForCapture();
            if (presentation.WasVisible) Thread.Sleep(65);

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return CopyBitmapToClipboard(bitmap);
        }
        catch
        {
            return false;
        }
        finally
        {
            presentation.Restore();
        }
    }

    private static bool LooksBlank(Bitmap bitmap)
    {
        if (bitmap.Width == 0 || bitmap.Height == 0) return true;
        var stepX = Math.Max(1, bitmap.Width / 12);
        var stepY = Math.Max(1, bitmap.Height / 12);
        var maximum = 0;
        var minimum = 255;
        var sampled = 0;
        for (var y = stepY / 2; y < bitmap.Height; y += stepY)
        {
            for (var x = stepX / 2; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var luminance = (color.R + color.G + color.B) / 3;
                maximum = Math.Max(maximum, luminance);
                minimum = Math.Min(minimum, luminance);
                sampled++;
            }
        }
        return sampled == 0 || (maximum < 10 && maximum - minimum < 4);
    }

    private static bool CopyBitmapToClipboard(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            // Clipboard contention is common during calls/IDE use. Retry briefly instead of failing the hotkey immediately.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetImage(source);
                    return true;
                }
                catch (COMException) when (attempt < 4)
                {
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
            return false;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private readonly record struct QwenPresentationSnapshot(IntPtr Hwnd, bool WasVisible, bool WasMinimized, bool WasMaximized)
    {
        public static QwenPresentationSnapshot Capture(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return new(IntPtr.Zero, false, false, false);
            return new(hwnd, Native.IsWindowVisible(hwnd), Native.IsIconic(hwnd), Native.IsZoomed(hwnd));
        }

        public void HideForCapture()
        {
            if (WasVisible && Hwnd != IntPtr.Zero && Native.IsWindow(Hwnd)) Native.ShowWindowAsync(Hwnd, Native.SW_HIDE);
        }

        public void Restore()
        {
            if (!WasVisible || Hwnd == IntPtr.Zero || !Native.IsWindow(Hwnd)) return;
            var command = WasMinimized ? Native.SW_SHOWMINIMIZED : WasMaximized ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOWNOACTIVATE;
            Native.ShowWindowAsync(Hwnd, command);
        }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
