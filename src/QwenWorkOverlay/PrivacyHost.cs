using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QwenWorkOverlay;

/// <summary>
/// A deliberately small, controller-owned top-level HWND.  It hosts the real Qwen HWND only after
/// display affinity and the on-disk recovery journal have both been verified.  This is not a WebView
/// or a replacement UI: the child is the installed Qwen Desktop window.
/// </summary>
internal sealed class PrivacyHostWindow : Window
{
    private IntPtr _child;

    public PrivacyHostWindow(Native.RECT qwenRect, uint qwenDpi)
    {
        var scale = qwenDpi == 0 ? 1d : 96d / qwenDpi;
        Title = "Qwen Privacy Host";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        AllowsTransparency = false; // WDA must target a normal top-level HWND, not a layered WPF window.
        Background = System.Windows.Media.Brushes.Black;
        Left = qwenRect.Left * scale;
        Top = qwenRect.Top * scale;
        Width = Math.Max(1, (qwenRect.Right - qwenRect.Left) * scale);
        Height = Math.Max(1, (qwenRect.Bottom - qwenRect.Top) * scale);
        SizeChanged += (_, _) => ResizeChild();
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public void AttachChild(IntPtr child)
    {
        _child = child;
        ResizeChild();
    }

    private void ResizeChild()
    {
        if (_child == IntPtr.Zero || !Native.IsWindow(_child) || Hwnd == IntPtr.Zero) return;
        if (!Native.GetWindowRect(Hwnd, out var hostRect)) return;
        var width = Math.Max(1, hostRect.Right - hostRect.Left);
        var height = Math.Max(1, hostRect.Bottom - hostRect.Top);
        Native.SetWindowPos(_child, IntPtr.Zero, 0, 0, width, height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
    }
}

internal sealed class PrivacyHostSession : IDisposable
{
    private readonly AppLogger _log;
    private readonly WindowRecoveryService _recovery;
    private PrivacyHostWindow? _host;
    private IntPtr _hostHwnd;
    private QwenTarget? _target;

    public PrivacyHostSession(AppLogger log, WindowRecoveryService recovery)
    {
        _log = log;
        _recovery = recovery;
    }

    public CapturePrivacyState State { get; private set; } = CapturePrivacyState.Off;
    public string Status { get; private set; } = "OFF (privacy host not enabled)";
    // Reading WindowInteropHelper.Handle requires the WPF owner thread. Cache the verified HWND
    // while constructing the host so diagnostics and asynchronous capture probes never cross it.
    public IntPtr HostHwnd => _hostHwnd;
    public IntPtr QwenChildHwnd => _target?.Hwnd ?? IntPtr.Zero;
    public IntPtr OriginalParent { get; private set; }
    public IntPtr CurrentParent => _target is not null && Native.IsWindow(_target.Hwnd) ? Native.GetParent(_target.Hwnd) : IntPtr.Zero;
    public uint RequestedAffinity { get; private set; }
    public uint VerifiedAffinity { get; private set; }
    public uint HostDpi { get; private set; }
    public uint QwenDpi { get; private set; }
    public bool DwmCompositionEnabled { get; private set; }
    public IntPtr HostDpiAwarenessContext { get; private set; }
    public IntPtr QwenDpiAwarenessContext { get; private set; }
    public CapturePathProbeResult GdiProbe { get; private set; } = CapturePathProbeResult.NotRun;
    public NativeCaptureProbeResult DesktopDuplicationProbe { get; private set; } = NativeCaptureProbeResult.NotRun;
    public NativeCaptureProbeResult WindowsGraphicsCaptureProbe { get; private set; } = NativeCaptureProbeResult.NotRun;

    public bool TryEnable(QwenTarget target)
    {
        if (State == CapturePrivacyState.Enabled && _target?.Hwnd == target.Hwnd) return true;
        if (State == CapturePrivacyState.Enabled) return false;
        State = CapturePrivacyState.Hosting;
        Status = "Preparing controller-owned privacy host";
        _target = target;
        RequestedAffinity = Native.WDA_EXCLUDEFROMCAPTURE;
        VerifiedAffinity = Native.WDA_NONE;

        try
        {
            if (!Native.IsWindow(target.Hwnd) || !Native.IsWindowVisible(target.Hwnd) || Native.IsIconic(target.Hwnd) ||
                !Native.GetWindowRect(target.Hwnd, out var rect) || Native.IsMinimizedCoordinate(rect))
                return Fail("Qwen is not a visible, restorable top-level window");

            OriginalParent = Native.GetParent(target.Hwnd);
            QwenDpi = Native.GetDpiForWindow(target.Hwnd);
            if (QwenDpi == 0) return Fail("GetDpiForWindow(Qwen) failed; refusing DPI-unsafe reparenting");
            QwenDpiAwarenessContext = Native.GetWindowDpiAwarenessContext(target.Hwnd);
            if (QwenDpiAwarenessContext == IntPtr.Zero) return Fail("Qwen DPI-awareness context is unavailable; refusing cross-process reparenting");
            if (!_recovery.HasPendingSnapshot) return Fail("Verified recovery journal is absent");

            _host = new PrivacyHostWindow(rect, QwenDpi);
            _host.Closed += HostClosed;
            _host.Show();
            var hostHwnd = _host.Hwnd;
            if (hostHwnd == IntPtr.Zero || !Native.IsWindow(hostHwnd)) return Fail("Privacy host HWND was not created");
            _hostHwnd = hostHwnd;

            DwmCompositionEnabled = Native.IsDesktopCompositionEnabled();
            if (!DwmCompositionEnabled) return Fail("Desktop Window Manager composition is unavailable; capture affinity cannot be trusted");

            HostDpi = Native.GetDpiForWindow(hostHwnd);
            if (!PrivacyHostPolicy.IsDpiCompatible(HostDpi, QwenDpi))
                return Fail($"DPI mismatch: host={HostDpi}, Qwen={QwenDpi}");
            HostDpiAwarenessContext = Native.GetWindowDpiAwarenessContext(hostHwnd);
            if (!PrivacyHostPolicy.IsDpiAwarenessCompatible(HostDpiAwarenessContext, QwenDpiAwarenessContext))
                return Fail($"DPI-awareness mismatch: host={Native.DescribeDpiAwarenessContext(HostDpiAwarenessContext)}, Qwen={Native.DescribeDpiAwarenessContext(QwenDpiAwarenessContext)}");

            Marshal.SetLastPInvokeError(0);
            if (!Native.SetWindowDisplayAffinity(hostHwnd, RequestedAffinity))
                return Fail("SetWindowDisplayAffinity(host) failed; win32=" + Marshal.GetLastWin32Error());
            if (!Native.GetWindowDisplayAffinity(hostHwnd, out var affinity) || !PrivacyHostPolicy.IsVerifiedAffinity(RequestedAffinity, affinity))
                return Fail("GetWindowDisplayAffinity(host) did not verify WDA_EXCLUDEFROMCAPTURE");
            VerifiedAffinity = affinity;

            if (!_recovery.MarkPrivacyHost(hostHwnd, HostDpi))
                return Fail("Privacy-host recovery journal verification failed");

            var style = Native.GetWindowLongPtr(target.Hwnd, Native.GWL_STYLE).ToInt64();
            var childStyle = PrivacyHostPolicy.ToChildStyle(style);
            Marshal.SetLastPInvokeError(0);
            Native.SetWindowLongPtr(target.Hwnd, Native.GWL_STYLE, new IntPtr(childStyle));
            if (Marshal.GetLastWin32Error() != 0) return Fail("Could not apply required WS_CHILD style; win32=" + Marshal.GetLastWin32Error());

            Native.SetParent(target.Hwnd, hostHwnd);
            if (Native.GetParent(target.Hwnd) != hostHwnd)
                return Fail("SetParent did not make Qwen a child of the privacy host; win32=" + Marshal.GetLastWin32Error());

            _host.AttachChild(target.Hwnd);
            State = CapturePrivacyState.Enabled;
            GdiProbe = CapturePathProbeResult.NotRun;
            DesktopDuplicationProbe = NativeCaptureProbeResult.NotRun;
            WindowsGraphicsCaptureProbe = NativeCaptureProbeResult.NotRun;
            Status = "ON (host affinity verified; capture compatibility still requires per-pipeline validation)";
            _log.Info($"Privacy host enabled: host=0x{hostHwnd.ToInt64():X}, qwen=0x{target.Hwnd.ToInt64():X}, dpi={HostDpi}, dpiAwareness={Native.DescribeDpiAwarenessContext(HostDpiAwarenessContext)}, affinity=0x{affinity:X}");
            return true;
        }
        catch (Exception ex)
        {
            return Fail("Privacy host exception: " + ex.GetType().Name);
        }
    }

    public CapturePathProbeResult ValidateGdiScreenCopy()
    {
        if (State != CapturePrivacyState.Enabled || HostHwnd == IntPtr.Zero)
        {
            GdiProbe = new CapturePathProbeResult(CaptureProbeVerdict.Failed, 0, 0, 0,
                "GDI probe requires an active verified privacy host");
            return GdiProbe;
        }

        GdiProbe = CapturePathProbe.ValidateGdiScreenCopy(HostHwnd);
        _log.Info($"Privacy GDI capture probe: verdict={GdiProbe.Verdict}, difference={GdiProbe.MeanRgbDifference:F1}, visibleVariance={GdiProbe.VisibleVariance:F1}, hiddenVariance={GdiProbe.HiddenVariance:F1}");
        return GdiProbe;
    }

    public async Task<(NativeCaptureProbeResult DesktopDuplication, NativeCaptureProbeResult WindowsGraphicsCapture)> ValidateNativeCapturePathsAsync()
    {
        if (State != CapturePrivacyState.Enabled || HostHwnd == IntPtr.Zero)
        {
            var failure = new NativeCaptureProbeResult(CaptureProbeVerdict.Failed,
                "Native capture probes require an active verified privacy host");
            DesktopDuplicationProbe = failure;
            WindowsGraphicsCaptureProbe = failure;
            return (failure, failure);
        }

        DesktopDuplicationProbe = await NativeCaptureProbeRunner.RunAsync(
            "privacy-capture-probe.exe", "RESULT DesktopDuplication=", HostHwnd);
        WindowsGraphicsCaptureProbe = await NativeCaptureProbeRunner.RunAsync(
            "privacy-wgc-capture-probe.exe", "RESULT WindowsGraphicsCapture=", HostHwnd);
        _log.Info("Privacy Desktop Duplication probe: " + DesktopDuplicationProbe.Detail);
        _log.Info("Privacy Windows Graphics Capture probe: " + WindowsGraphicsCaptureProbe.Detail);
        return (DesktopDuplicationProbe, WindowsGraphicsCaptureProbe);
    }

    // Once recovery has completed, the controller must obtain a new journal before it mutates Qwen
    // again. This clears only the failed session bookkeeping; it never restores or changes Qwen.
    public void ResetAfterVerifiedRecovery()
    {
        if (State != CapturePrivacyState.Failed) return;
        CloseHost();
        _target = null;
        OriginalParent = IntPtr.Zero;
        RequestedAffinity = Native.WDA_NONE;
        VerifiedAffinity = Native.WDA_NONE;
        HostDpi = 0;
        QwenDpi = 0;
        DwmCompositionEnabled = false;
        HostDpiAwarenessContext = IntPtr.Zero;
        QwenDpiAwarenessContext = IntPtr.Zero;
        GdiProbe = CapturePathProbeResult.NotRun;
        DesktopDuplicationProbe = NativeCaptureProbeResult.NotRun;
        WindowsGraphicsCaptureProbe = NativeCaptureProbeResult.NotRun;
        State = CapturePrivacyState.Off;
        Status = "OFF (Qwen must be reacquired after privacy recovery)";
    }

    public bool DisableAndRestore()
    {
        if (_target is null) return true;
        if (!Native.IsWindow(_target.Hwnd))
        {
            // Qwen exited while hosted. There is no live HWND to restore; discard only the journal
            // after its normal PID/HWND validation and close our host so a restarted Qwen is never
            // left parented to a stale controller window.
            _recovery.TryRecoverStaleState();
            CloseHost();
            _target = null;
            State = CapturePrivacyState.Off;
            Status = "OFF (Qwen exited; host closed)";
            return true;
        }
        var restored = _recovery.TryRecoverStaleState();
        if (!restored)
        {
            State = CapturePrivacyState.Failed;
            Status = "FAILED: Qwen parent/style restoration was not verified";
            return false;
        }
        CloseHost();
        State = CapturePrivacyState.Off;
        Status = "OFF (Qwen restored to its original top-level state)";
        _target = null;
        return true;
    }

    private bool Fail(string reason)
    {
        _log.Error("Privacy host refused/rolled back: " + reason);
        Status = "FAILED: " + reason;
        State = CapturePrivacyState.Failed;
        if (_target is not null && _recovery.HasPendingSnapshot)
            _recovery.TryRecoverStaleState();
        CloseHost();
        _target = null;
        return false;
    }

    private void HostClosed(object? sender, EventArgs e)
    {
        if (State != CapturePrivacyState.Enabled) return;
        _log.Error("Privacy host closed unexpectedly; attempting Qwen restore");
        _hostHwnd = IntPtr.Zero;
        State = CapturePrivacyState.Failed;
        Status = "FAILED: privacy host closed unexpectedly";
        _recovery.TryRecoverStaleState();
        _target = null;
    }

    private void CloseHost()
    {
        var host = _host;
        _host = null;
        _hostHwnd = IntPtr.Zero;
        if (host is null) return;
        host.Closed -= HostClosed;
        try { host.Close(); } catch { }
    }

    public void Dispose()
    {
        if (State == CapturePrivacyState.Enabled || State == CapturePrivacyState.Hosting)
            DisableAndRestore();
        else
            CloseHost();
    }
}
