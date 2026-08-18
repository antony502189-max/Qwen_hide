using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ChatGPTDesktopController;

public sealed record ScreenshotResult(bool Success, string Detail, DateTimeOffset At);

public static class ScreenshotService
{
    public static ScreenshotResult CaptureActiveMonitorToClipboard(IntPtr _)
    {
        var foreground = Native.GetForegroundWindow();
        var screen = foreground != IntPtr.Zero ? Forms.Screen.FromHandle(foreground) : Forms.Screen.PrimaryScreen;
        if (screen is null) return new(false, "No monitor could be resolved.", DateTimeOffset.Now);

        var bounds = screen.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return new(false, "Resolved monitor has invalid bounds.", DateTimeOffset.Now);

        try
        {
            // Do not hide/show ChatGPT around F6. A visibility transition can make Electron briefly
            // re-register or repaint the window and can create a capture-visible flash. MainWindow
            // verifies WDA_EXCLUDEFROMCAPTURE synchronously before reaching this method, so the monitor
            // capture is taken while the protected ChatGPT HWND remains continuously visible locally.
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);

            return PutInClipboard(bitmap)
                ? new(true, $"Full monitor screenshot copied to Clipboard without toggling ChatGPT visibility: {screen.DeviceName} {bounds.Width}x{bounds.Height}.", DateTimeOffset.Now)
                : new(false, "Clipboard is busy; retry F6.", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            return new(false, "Capture failed: " + ex.GetType().Name, DateTimeOffset.Now);
        }
    }

    // Compatibility alias for older call sites/tests. Semantics are intentionally full-monitor now.
    public static ScreenshotResult CaptureActiveWindowToClipboard(IntPtr controllerTarget) => CaptureActiveMonitorToClipboard(controllerTarget);

    private static bool PutInClipboard(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            for (var i = 0; i < 7; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetImage(source);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(25 * (i + 1));
                }
            }
            return false;
        }
        finally
        {
            Native.DeleteObject(hBitmap);
        }
    }

    public static bool ClipboardContainsImage()
    {
        try { return System.Windows.Clipboard.ContainsImage(); }
        catch { return false; }
    }
}
