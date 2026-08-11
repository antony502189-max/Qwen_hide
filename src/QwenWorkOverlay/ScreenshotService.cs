using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QwenWorkOverlay;

public static class ScreenshotService
{
    public static bool CaptureWindowToClipboard(IntPtr hwnd)
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

            if (!printed)
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
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
        var qwenWasVisible = qwenHwnd != IntPtr.Zero && Native.IsWindow(qwenHwnd) && Native.IsWindowVisible(qwenHwnd);
        try
        {
            // The native Qwen window cannot safely use WDA_EXCLUDEFROMCAPTURE from this companion process.
            // Briefly hide it only for our own full-monitor screenshot so it does not become part of the clipboard image.
            if (qwenWasVisible)
            {
                Native.ShowWindowAsync(qwenHwnd, Native.SW_HIDE);
                Thread.Sleep(70);
            }

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
            if (qwenWasVisible) Native.ShowWindowAsync(qwenHwnd, Native.SW_SHOW);
        }
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
            System.Windows.Clipboard.SetImage(source);
            return true;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
