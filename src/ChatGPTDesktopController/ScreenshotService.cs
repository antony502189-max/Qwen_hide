using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ChatGPTDesktopController;

public sealed record ScreenshotResult(bool Success, string Detail, DateTimeOffset At);

public static class ScreenshotService
{
    public static ScreenshotResult CaptureActiveWindowToClipboard(IntPtr controllerTarget)
    {
        var hwnd = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return new(false, "No active top-level window.", DateTimeOffset.Now);
        if (!Native.GetWindowRect(hwnd, out var r) || r.Right <= r.Left || r.Bottom <= r.Top) return new(false, "Active window has no capturable bounds.", DateTimeOffset.Now);
        var targetWasVisible = controllerTarget != IntPtr.Zero && hwnd == controllerTarget && Native.IsWindowVisible(controllerTarget);
        try
        {
            if (targetWasVisible) Native.ShowWindowAsync(controllerTarget, Native.SW_HIDE);
            if (targetWasVisible) Thread.Sleep(70);
            using var bitmap = new Bitmap(r.Right-r.Left, r.Bottom-r.Top, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            return PutInClipboard(bitmap) ? new(true, "Active window copied as native clipboard image.", DateTimeOffset.Now) : new(false, "Clipboard is busy; retry F6.", DateTimeOffset.Now);
        }
        catch (Exception ex) { return new(false, "Capture failed: " + ex.GetType().Name, DateTimeOffset.Now); }
        finally { if (targetWasVisible) Native.ShowWindowAsync(controllerTarget, Native.SW_RESTORE); }
    }
    private static bool PutInClipboard(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze();
            for (var i = 0; i != 5; ++i) try { System.Windows.Clipboard.SetImage(source); return true; } catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(25 * (i + 1)); }
            return false;
        }
        finally { Native.DeleteObject(hBitmap); }
    }
    public static bool ClipboardContainsImage()
    {
        try { return System.Windows.Clipboard.ContainsImage(); } catch { return false; }
    }
}
