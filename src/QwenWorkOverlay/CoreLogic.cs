namespace QwenWorkOverlay;

public static class VirtualMixOutputPolicy
{
    public static bool IsRecognizedVirtualName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var value = name.Trim().ToLowerInvariant();

        // Strong vendor/product markers are safe enough to accept even when Windows adds generic endpoint words.
        var strongVirtual = new[]
        {
            "vb-audio",
            "voicemeeter",
            "virtual audio cable",
            "virtual cable",
            "cable input",
            "cable-a input",
            "cable-b input",
            "cable-c input",
            "blackhole"
        };
        if (strongVirtual.Any(value.Contains)) return true;

        // Generic "virtual"/"loopback" labels alone are not enough when the same name clearly describes
        // physical playback hardware. Sending the mixed stream there could create an acoustic feedback loop.
        var genericVirtual = value.Contains("virtual") || value.Contains("loopback");
        if (!genericVirtual) return false;

        var physicalMarkers = new[]
        {
            "speaker",
            "headphone",
            "headset",
            "realtek",
            "nvidia high definition",
            "display audio",
            "hdmi",
            "bluetooth",
            "airpods",
            "soundbar",
            "monitor audio",
            "usb audio"
        };
        return !physicalMarkers.Any(value.Contains);
    }
}

public enum ClipboardPayloadKind { Empty, Text, Image }

public static class ClipboardPolicy
{
    public static ClipboardPayloadKind Classify(bool hasText, bool hasImage) =>
        hasText ? ClipboardPayloadKind.Text : hasImage ? ClipboardPayloadKind.Image : ClipboardPayloadKind.Empty;
}

public sealed class RightCtrlStateMachine
{
    public bool IsDown { get; private set; }

    public bool OnDown()
    {
        if (IsDown) return false;
        IsDown = true;
        return true;
    }

    public bool OnUp()
    {
        if (!IsDown) return false;
        IsDown = false;
        return true;
    }
}

public static class NativeCapturePrivacyPolicy
{
    // SetWindowDisplayAffinity only applies safely when the top-level target window belongs to the calling process.
    public static bool CanApplyDirectly(int controllerProcessId, int targetProcessId) => controllerProcessId == targetProcessId;
}

public enum PrivacyHostTransition
{
    Off,
    Preparing,
    Enabled,
    Failed,
    Restored
}

public static class PrivacyHostPolicy
{
    public static long ToChildStyle(long currentStyle) => (currentStyle | Native.WS_CHILD) & ~Native.WS_POPUP;
    public static bool IsDpiCompatible(uint hostDpi, uint qwenDpi) => hostDpi != 0 && hostDpi == qwenDpi;
    public static bool IsVerifiedAffinity(uint requested, uint verified) =>
        requested == Native.WDA_EXCLUDEFROMCAPTURE && verified == requested;
    public static bool IsDpiAwarenessCompatible(IntPtr hostContext, IntPtr qwenContext) =>
        hostContext != IntPtr.Zero && qwenContext != IntPtr.Zero && Native.AreDpiAwarenessContextsEqual(hostContext, qwenContext);
}

public readonly record struct PrivacyHostBounds(double Left, double Top, double Width, double Height);

public static class PrivacyHostGeometryPolicy
{
    // WPF sizes are DIPs while GetWindowRect supplies physical pixels. This conversion is used
    // only after the host/Qwen DPI compatibility gate has established a non-zero shared DPI.
    public static PrivacyHostBounds FromQwenPhysicalBounds(int left, int top, int right, int bottom, uint dpi)
    {
        if (dpi == 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        var scale = 96d / dpi;
        return new PrivacyHostBounds(
            left * scale,
            top * scale,
            Math.Max(1, (right - left) * scale),
            Math.Max(1, (bottom - top) * scale));
    }
}

public enum CaptureProbeVerdict
{
    NotRun,
    RedactedPlaceholder,
    Exposed,
    Inconclusive,
    Failed
}

public static class CaptureProbePolicy
{
    // A plain screen copy is only one capture pipeline. The thresholds deliberately leave a broad
    // inconclusive range so transient Qwen animation or a uniform desktop cannot become a privacy claim.
    public static CaptureProbeVerdict ClassifyGdi(double meanRgbDifference, double visibleVariance, double hiddenVariance)
    {
        if (!double.IsFinite(meanRgbDifference) || !double.IsFinite(visibleVariance) || !double.IsFinite(hiddenVariance))
            return CaptureProbeVerdict.Failed;
        if (visibleVariance < 6 && hiddenVariance < 6) return CaptureProbeVerdict.Inconclusive;
        // A matching visible/hidden sample can be a coincidentally uniform Qwen view or desktop;
        // it never proves that a capture pipeline excluded the host.
        if (meanRgbDifference <= 4) return CaptureProbeVerdict.Inconclusive;
        if (visibleVariance < 6 && hiddenVariance >= 6 && meanRgbDifference >= 18) return CaptureProbeVerdict.RedactedPlaceholder;
        if (meanRgbDifference >= 18) return CaptureProbeVerdict.Exposed;
        return CaptureProbeVerdict.Inconclusive;
    }

    // PrintWindow asks the target to render directly into a supplied DC. It is not equivalent to
    // a full-monitor share: a non-uniform successful sample is positive evidence of direct capture.
    public static CaptureProbeVerdict ClassifyPrintWindow(double visibleVariance)
    {
        if (!double.IsFinite(visibleVariance)) return CaptureProbeVerdict.Failed;
        return visibleVariance >= 6 ? CaptureProbeVerdict.Exposed : CaptureProbeVerdict.Inconclusive;
    }
}

public static class CapturePrivacyStatusPolicy
{
    public static string Build(
        CaptureProbeVerdict gdi,
        CaptureProbeVerdict printWindow,
        CaptureProbeVerdict desktopDuplication,
        CaptureProbeVerdict windowsGraphicsCapture)
    {
        var exposed = new List<string>();
        if (gdi == CaptureProbeVerdict.Exposed) exposed.Add("GDI");
        if (printWindow == CaptureProbeVerdict.Exposed) exposed.Add("PrintWindow");
        if (desktopDuplication == CaptureProbeVerdict.Exposed) exposed.Add("Desktop Duplication");
        if (windowsGraphicsCapture == CaptureProbeVerdict.Exposed) exposed.Add("Windows Graphics Capture");

        return exposed.Count > 0
            ? "ACTIVE — CAPTURE EXPOSED by " + string.Join(", ", exposed) + "; do not share Qwen"
            : "ACTIVE — host WDA verified; capture results are pipeline-specific and do not certify conferencing apps";
    }
}

public static class PrivacyMutationPolicy
{
    public static bool CanMutateNativeWindow(bool nativeHwndExists, CapturePrivacyState privacyState) =>
        nativeHwndExists && privacyState != CapturePrivacyState.Failed;
}

public static class WindowStylePolicy
{
    public static bool NeedsLayeredWindow(double opacity, bool clickThrough) => opacity < .999 || clickThrough;

    public static long ComputeExtendedStyle(long originalStyle, double opacity, bool clickThrough)
    {
        var desired = originalStyle;
        if (NeedsLayeredWindow(opacity, clickThrough)) desired |= Native.WS_EX_LAYERED;
        else if ((originalStyle & Native.WS_EX_LAYERED) == 0) desired &= ~Native.WS_EX_LAYERED;

        if (clickThrough) desired |= Native.WS_EX_TRANSPARENT;
        else if ((originalStyle & Native.WS_EX_TRANSPARENT) == 0) desired &= ~Native.WS_EX_TRANSPARENT;
        return desired;
    }
}
