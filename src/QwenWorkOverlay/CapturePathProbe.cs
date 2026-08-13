using System.Drawing;
using System.Threading;

namespace QwenWorkOverlay;

public sealed record CapturePathProbeResult(
    CaptureProbeVerdict Verdict,
    double MeanRgbDifference,
    double VisibleVariance,
    double HiddenVariance,
    string Detail)
{
    public static readonly CapturePathProbeResult NotRun = new(CaptureProbeVerdict.NotRun, 0, 0, 0, "Not run");
}

/// <summary>
/// Tests only the legacy GDI screen-copy pipeline. It intentionally collects no image data on disk
/// and retains only aggregate pixel statistics for diagnostics.
/// </summary>
internal static class CapturePathProbe
{
    public static CapturePathProbeResult ValidateGdiScreenCopy(IntPtr hostHwnd)
    {
        if (hostHwnd == IntPtr.Zero || !Native.IsWindow(hostHwnd) || !Native.IsWindowVisible(hostHwnd) ||
            !Native.GetWindowRect(hostHwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return new(CaptureProbeVerdict.Failed, 0, 0, 0, "Privacy host is not visible for GDI validation");

        // Keep the probe bounded and avoid a full-monitor allocation; a central 96x96 patch is enough
        // to distinguish a captured host from the identical region behind it in ordinary conditions.
        const int sampleSize = 96;
        var centerX = rect.Left + (rect.Right - rect.Left) / 2;
        var centerY = rect.Top + (rect.Bottom - rect.Top) / 2;
        var left = centerX - sampleSize / 2;
        var top = centerY - sampleSize / 2;
        var wasForeground = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == hostHwnd;

        try
        {
            using var visible = Capture(left, top, sampleSize, sampleSize);
            Native.ShowWindowAsync(hostHwnd, Native.SW_HIDE);
            Thread.Sleep(90);
            using var hidden = Capture(left, top, sampleSize, sampleSize);
            Native.ShowWindowAsync(hostHwnd, Native.SW_SHOWNOACTIVATE);
            if (wasForeground) Native.SetForegroundWindow(hostHwnd);

            var visibleVariance = Variance(visible);
            var hiddenVariance = Variance(hidden);
            var difference = MeanDifference(visible, hidden);
            var verdict = CaptureProbePolicy.ClassifyGdi(difference, visibleVariance, hiddenVariance);
            var detail = verdict switch
            {
                CaptureProbeVerdict.LikelyExcluded => "GDI screen copy likely saw the background rather than the privacy host",
                CaptureProbeVerdict.Exposed => "GDI screen copy changed when the privacy host was hidden (host content was captured)",
                _ => "GDI screen copy result is inconclusive; no capture-privacy claim"
            };
            return new(verdict, difference, visibleVariance, hiddenVariance, detail);
        }
        catch (Exception ex)
        {
            try { Native.ShowWindowAsync(hostHwnd, Native.SW_SHOWNOACTIVATE); } catch { }
            return new(CaptureProbeVerdict.Failed, 0, 0, 0, "GDI capture probe failed: " + ex.GetType().Name);
        }
    }

    private static Bitmap Capture(int left, int top, int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static double MeanDifference(Bitmap first, Bitmap second)
    {
        double sum = 0;
        var count = 0;
        for (var y = 0; y < first.Height; y += 4)
        for (var x = 0; x < first.Width; x += 4)
        {
            var a = first.GetPixel(x, y);
            var b = second.GetPixel(x, y);
            sum += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3d;
            count++;
        }
        return count == 0 ? double.NaN : sum / count;
    }

    private static double Variance(Bitmap bitmap)
    {
        var values = new List<double>();
        for (var y = 0; y < bitmap.Height; y += 4)
        for (var x = 0; x < bitmap.Width; x += 4)
        {
            var c = bitmap.GetPixel(x, y);
            values.Add((c.R + c.G + c.B) / 3d);
        }
        if (values.Count == 0) return double.NaN;
        var mean = values.Average();
        return values.Average(value => (value - mean) * (value - mean));
    }
}
