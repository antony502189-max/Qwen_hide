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

        var wasForeground = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == hostHwnd;

        try
        {
            var visible = CaptureDistributedSample(rect);
            Native.ShowWindowAsync(hostHwnd, Native.SW_HIDE);
            Thread.Sleep(90);
            var hidden = CaptureDistributedSample(rect);
            Native.ShowWindowAsync(hostHwnd, Native.SW_SHOWNOACTIVATE);
            if (wasForeground) Native.SetForegroundWindow(hostHwnd);

            var visibleVariance = Variance(visible);
            var hiddenVariance = Variance(hidden);
            var difference = MeanDifference(visible, hidden);
            var verdict = CaptureProbePolicy.ClassifyGdi(difference, visibleVariance, hiddenVariance);
            var detail = verdict switch
            {
                CaptureProbeVerdict.RedactedPlaceholder => "GDI screen copy saw a uniform redacted surface, not strict host absence",
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

    // Four bounded patches (rather than a centre-only image) are sampled across Qwen's actual
    // content. This avoids declaring an animated or empty chat centre a capture result.
    private static PixelSample CaptureDistributedSample(Native.RECT rect)
    {
        const int patchSize = 96;
        var sample = new PixelSample();
        foreach (var (horizontal, vertical) in new[] { (.2, .2), (.8, .2), (.2, .8), (.8, .8) })
        {
            var centerX = rect.Left + (int)((rect.Right - rect.Left) * horizontal);
            var centerY = rect.Top + (int)((rect.Bottom - rect.Top) * vertical);
            using var bitmap = new Bitmap(patchSize, patchSize, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(centerX - patchSize / 2, centerY - patchSize / 2, 0, 0,
                    new Size(patchSize, patchSize), CopyPixelOperation.SourceCopy);
            for (var y = 0; y < bitmap.Height; y += 4)
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var color = bitmap.GetPixel(x, y);
                sample.Pixels.Add((color.R, color.G, color.B));
            }
        }
        return sample;
    }

    private static double MeanDifference(PixelSample first, PixelSample second)
    {
        if (first.Pixels.Count != second.Pixels.Count || first.Pixels.Count == 0) return double.NaN;
        double sum = 0;
        for (var index = 0; index < first.Pixels.Count; index++)
        {
            var a = first.Pixels[index];
            var b = second.Pixels[index];
            sum += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3d;
        }
        return sum / first.Pixels.Count;
    }

    private static double Variance(PixelSample sample)
    {
        var values = sample.Pixels.Select(value => (value.R + value.G + value.B) / 3d).ToArray();
        if (values.Length == 0) return double.NaN;
        var mean = values.Average();
        return values.Average(value => (value - mean) * (value - mean));
    }

    private sealed class PixelSample
    {
        public List<(byte R, byte G, byte B)> Pixels { get; } = new();
    }
}
