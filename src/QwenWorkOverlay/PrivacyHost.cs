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

    public event EventHandler? ChildResizeFailed;

    public PrivacyHostWindow(Native.RECT qwenRect, uint qwenDpi)
    {
        var bounds = PrivacyHostGeometryPolicy.FromQwenPhysicalBounds(
            qwenRect.Left, qwenRect.Top, qwenRect.Right, qwenRect.Bottom, qwenDpi);
        Title = "Qwen Privacy Host";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        AllowsTransparency = false; // WDA must target a normal top-level HWND, not a layered WPF window.
        Background = System.Windows.Media.Brushes.Black;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        SizeChanged += (_, _) =>
        {
            if (!ResizeChild()) ChildResizeFailed?.Invoke(this, EventArgs.Empty);
        };
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public bool AttachChild(IntPtr child)
    {
        _child = child;
        return ResizeChild();
    }

    private bool ResizeChild()
    {
        if (_child == IntPtr.Zero) return true;
        if (!Native.IsWindow(_child) || Hwnd == IntPtr.Zero || !Native.IsWindow(Hwnd)) return false;
        if (!Native.GetWindowRect(Hwnd, out var hostRect)) return false;
        var width = Math.Max(1, hostRect.Right - hostRect.Left);
        var height = Math.Max(1, hostRect.Bottom - hostRect.Top);
        return Native.SetWindowPos(_child, IntPtr.Zero, 0, 0, width, height,
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
    public CapturePathProbeResult PrintWindowProbe { get; private set; } = CapturePathProbeResult.NotRun;
    public NativeCaptureProbeResult DesktopDuplicationProbe { get; private set; } = NativeCaptureProbeResult.NotRun;
    public NativeCaptureProbeResult WindowsGraphicsCaptureProbe { get; private set; } = NativeCaptureProbeResult.NotRun;

    public bool TryEnable(QwenTarget target)
    {
        if (!PrivacyHostPolicy.CrossProcessHostingSupportedOnTargetMachine)
            return MarkUnsupportedOnTargetMachine("cross-process SetParent did not preserve Qwen child resize behavior during staged validation");
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
            _host.ChildResizeFailed += HostChildResizeFailed;
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

            // Cross-process SetParent can reset/change the child DPI context. Re-read it after
            // parenting rather than assuming the pre-mutation value remains true.
            QwenDpi = Native.GetDpiForWindow(target.Hwnd);
            QwenDpiAwarenessContext = Native.GetWindowDpiAwarenessContext(target.Hwnd);
            if (!PrivacyHostPolicy.IsDpiCompatible(HostDpi, QwenDpi) ||
                !PrivacyHostPolicy.IsDpiAwarenessCompatible(HostDpiAwarenessContext, QwenDpiAwarenessContext))
                return Fail($"DPI state changed after SetParent: host={HostDpi}/{Native.DescribeDpiAwarenessContext(HostDpiAwarenessContext)}, Qwen={QwenDpi}/{Native.DescribeDpiAwarenessContext(QwenDpiAwarenessContext)}");

            if (!_host.AttachChild(target.Hwnd))
                return Fail("Could not size the Qwen child inside the privacy host; win32=" + Marshal.GetLastWin32Error());
            State = CapturePrivacyState.Enabled;
            GdiProbe = CapturePathProbeResult.NotRun;
            PrintWindowProbe = CapturePathProbeResult.NotRun;
            DesktopDuplicationProbe = NativeCaptureProbeResult.NotRun;
            WindowsGraphicsCaptureProbe = NativeCaptureProbeResult.NotRun;
            Status = "ACTIVE — host WDA verified; capture exclusion is not yet validated";
            _log.Info($"Privacy host enabled: host=0x{hostHwnd.ToInt64():X}, qwen=0x{target.Hwnd.ToInt64():X}, dpi={HostDpi}, dpiAwareness={Native.DescribeDpiAwarenessContext(HostDpiAwarenessContext)}, affinity=0x{affinity:X}");
            return true;
        }
        catch (Exception ex)
        {
            return Fail("Privacy host exception: " + ex.GetType().Name);
        }
    }

    public bool MarkUnsupportedOnTargetMachine(string reason)
    {
        // The target-machine staged test showed that a foreign Chromium Qwen child does not
        // reliably follow host resizing after SetParent. Do not leave this architecture callable
        // merely because the host can be created and WDA can be read back.
        State = CapturePrivacyState.UnsupportedForExternalWindow;
        Status = "UNSUPPORTED ON TARGET MACHINE: " + reason;
        _log.Error("Privacy host disabled: " + reason);
        return false;
    }

    public CapturePathProbeResult ValidateGdiScreenCopy()
    {
        if (State != CapturePrivacyState.Enabled || HostHwnd == IntPtr.Zero)
        {
            GdiProbe = new CapturePathProbeResult(CaptureProbeVerdict.Failed, 0, 0, 0,
                "GDI probe requires an active verified privacy host");
            return GdiProbe;
        }
        if (!ReverifyAffinity())
        {
            GdiProbe = new CapturePathProbeResult(CaptureProbeVerdict.Failed, 0, 0, 0,
                "Privacy host affinity changed before GDI validation");
            return GdiProbe;
        }

        GdiProbe = CapturePathProbe.ValidateGdiScreenCopy(HostHwnd);
        UpdateCaptureCompatibilityStatus();
        _log.Info($"Privacy GDI capture probe: verdict={GdiProbe.Verdict}, difference={GdiProbe.MeanRgbDifference:F1}, visibleVariance={GdiProbe.VisibleVariance:F1}, hiddenVariance={GdiProbe.HiddenVariance:F1}");
        return GdiProbe;
    }

    public CapturePathProbeResult ValidatePrintWindowCapture()
    {
        if (State != CapturePrivacyState.Enabled || HostHwnd == IntPtr.Zero)
        {
            PrintWindowProbe = new CapturePathProbeResult(CaptureProbeVerdict.Failed, 0, 0, 0,
                "PrintWindow probe requires an active verified privacy host");
            return PrintWindowProbe;
        }
        if (!ReverifyAffinity())
        {
            PrintWindowProbe = new CapturePathProbeResult(CaptureProbeVerdict.Failed, 0, 0, 0,
                "Privacy host affinity changed before PrintWindow validation");
            return PrintWindowProbe;
        }

        PrintWindowProbe = CapturePathProbe.ValidatePrintWindow(HostHwnd);
        UpdateCaptureCompatibilityStatus();
        _log.Info($"Privacy PrintWindow capture probe: verdict={PrintWindowProbe.Verdict}, visibleVariance={PrintWindowProbe.VisibleVariance:F1}");
        return PrintWindowProbe;
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
        if (!ReverifyAffinity())
        {
            var failure = new NativeCaptureProbeResult(CaptureProbeVerdict.Failed,
                "Privacy host affinity changed before native capture validation");
            DesktopDuplicationProbe = failure;
            WindowsGraphicsCaptureProbe = failure;
            return (failure, failure);
        }

        DesktopDuplicationProbe = await NativeCaptureProbeRunner.RunAsync(
            "privacy-capture-probe.exe", "RESULT DesktopDuplication=", HostHwnd);
        WindowsGraphicsCaptureProbe = await NativeCaptureProbeRunner.RunAsync(
            "privacy-wgc-capture-probe.exe", "RESULT WindowsGraphicsCapture=", HostHwnd);
        UpdateCaptureCompatibilityStatus();
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
        PrintWindowProbe = CapturePathProbeResult.NotRun;
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

    private bool ReverifyAffinity()
    {
        if (HostHwnd == IntPtr.Zero || !Native.GetWindowDisplayAffinity(HostHwnd, out var affinity) ||
            !PrivacyHostPolicy.IsVerifiedAffinity(RequestedAffinity, affinity))
        {
            return Fail("GetWindowDisplayAffinity(host) no longer verifies WDA_EXCLUDEFROMCAPTURE");
        }

        VerifiedAffinity = affinity;
        return true;
    }

    private void UpdateCaptureCompatibilityStatus()
    {
        Status = CapturePrivacyStatusPolicy.Build(
            GdiProbe.Verdict,
            PrintWindowProbe.Verdict,
            DesktopDuplicationProbe.Verdict,
            WindowsGraphicsCaptureProbe.Verdict);
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

    private void HostChildResizeFailed(object? sender, EventArgs e)
    {
        // Ignore construction-time notifications: TryEnable checks AttachChild synchronously and
        // rolls back there. During an active session, a failed resize means the hosted child is no
        // longer usable, so restore rather than leave Qwen trapped in a broken host.
        if (State == CapturePrivacyState.Enabled)
            Fail("Qwen child resize failed inside the privacy host; win32=" + Marshal.GetLastWin32Error());
    }

    private void CloseHost()
    {
        var host = _host;
        _host = null;
        _hostHwnd = IntPtr.Zero;
        if (host is null) return;
        host.Closed -= HostClosed;
        host.ChildResizeFailed -= HostChildResizeFailed;
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
