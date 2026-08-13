using System.Runtime.InteropServices;
using System.Windows.Threading;
using Xunit;

namespace QwenWorkOverlay.Tests;

/// <summary>
/// This test is intentionally excluded unless explicitly enabled on the target machine. It touches
/// the real installed Qwen HWND, but only through the same reversible public controller path users run.
/// It never runs in CI.
/// </summary>
public sealed class RealQwenPrivacyIntegrationTests
{
    [Fact]
    public void Real_qwen_privacy_host_verifies_affinity_and_restores_native_window()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("QDC_RUN_REAL_QWEN_PRIVACY_TESTS"), "1", StringComparison.Ordinal))
            return;
        if (!OperatingSystem.IsWindows()) return;

        Exception? error = null;
        var complete = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                RunProbe();
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try { Dispatcher.CurrentDispatcher.InvokeShutdown(); } catch { }
                complete.Set();
            }
        }) { IsBackground = true, Name = "QDC.RealQwenPrivacyTest" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(complete.Wait(TimeSpan.FromSeconds(30)), "Real Qwen privacy test timed out.");
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Real Qwen privacy test UI thread did not exit.");
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static void RunProbe()
    {
        var logger = new AppLogger();
        var target = new QwenProcessLocator(logger).FindRunningTarget();
        Assert.NotNull(target);
        var qwen = target!;
        Assert.True(Native.GetWindowRect(qwen.Hwnd, out var originalRect));
        Assert.False(Native.IsIconic(qwen.Hwnd));
        var originalParent = Native.GetParent(qwen.Hwnd);
        var originalStyle = Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_STYLE).ToInt64();
        var originalExStyle = Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_EXSTYLE).ToInt64();

        var tempRoot = Path.Combine(Path.GetTempPath(), "QdcRealQwenPrivacy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var journal = Path.Combine(tempRoot, "recovery.json");
        var controller = new QwenWindowController(logger, new WindowRecoveryService(logger, journal));
        try
        {
            Assert.True(controller.Attach(qwen, 1.0, false));
            if (!controller.EnablePrivacyHost())
            {
                Assert.Equal(CapturePrivacyState.UnsupportedForExternalWindow, controller.PrivacyState);
                Assert.Contains("UNSUPPORTED ON TARGET MACHINE", controller.PrivacyStatus);
                return;
            }
            Assert.Equal(CapturePrivacyState.Enabled, controller.PrivacyState);
            Assert.NotEqual(IntPtr.Zero, controller.PrivacyHostHwnd);
            Assert.Equal(controller.PrivacyHostHwnd, controller.CurrentParent);
            Assert.Equal(Native.WDA_EXCLUDEFROMCAPTURE, controller.RequestedAffinity);
            Assert.Equal(Native.WDA_EXCLUDEFROMCAPTURE, controller.VerifiedAffinity);
            Assert.True(controller.DwmCompositionEnabled);
            Assert.True(PrivacyHostPolicy.IsDpiCompatible(controller.HostDpi, controller.QwenDpi));
            Assert.True(PrivacyHostPolicy.IsDpiAwarenessCompatible(controller.HostDpiAwarenessContext, controller.QwenDpiAwarenessContext));

            // A hosted Qwen must track a controller-host resize rather than being left at stale
            // coordinates or dimensions. The final restoration assertion below proves this is
            // still fully reversible.
            Assert.True(Native.GetWindowRect(controller.PrivacyHostHwnd, out var hostRect));
            Assert.True(Native.GetWindowRect(qwen.Hwnd, out var hostedQwenRect));
            var resizedWidth = Math.Max(600, hostRect.Right - hostRect.Left - 96);
            var resizedHeight = Math.Max(400, hostRect.Bottom - hostRect.Top - 64);
            Assert.True(Native.SetWindowPos(controller.PrivacyHostHwnd, IntPtr.Zero,
                hostRect.Left, hostRect.Top, resizedWidth, resizedHeight,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
            PumpUntil(() => Native.GetWindowRect(qwen.Hwnd, out var resizedQwenRect) &&
                            (resizedQwenRect.Right - resizedQwenRect.Left) < (hostedQwenRect.Right - hostedQwenRect.Left) &&
                            (resizedQwenRect.Bottom - resizedQwenRect.Top) < (hostedQwenRect.Bottom - hostedQwenRect.Top),
                TimeSpan.FromSeconds(3));
            Assert.True(Native.GetWindowRect(qwen.Hwnd, out var finalHostedQwenRect));
            Assert.True(finalHostedQwenRect.Right - finalHostedQwenRect.Left < hostedQwenRect.Right - hostedQwenRect.Left);
            Assert.True(finalHostedQwenRect.Bottom - finalHostedQwenRect.Top < hostedQwenRect.Bottom - hostedQwenRect.Top);

            var gdiProbe = controller.ValidatePrivacyGdiCapture();
            Assert.NotEqual(CaptureProbeVerdict.NotRun, gdiProbe.Verdict);
            Assert.NotEqual(CaptureProbeVerdict.Failed, gdiProbe.Verdict);

            // PrintWindow is a distinct direct-window capture path. A platform/application result
            // may be inconclusive, but the probe itself must run and report that fact.
            var printWindowProbe = controller.ValidatePrivacyPrintWindowCapture();
            Assert.NotEqual(CaptureProbeVerdict.NotRun, printWindowProbe.Verdict);

            StageAndRunOptionalNativeCaptureProbes(controller);

            // Exercise the same restoration path used if the controller-owned host disappears.
            // This validates that Qwen cannot remain parented to a dead host and that the controller
            // invalidates its old mutation lease afterwards.
            Assert.True(Native.PostMessage(controller.PrivacyHostHwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero));
            PumpUntil(() => controller.PrivacyState == CapturePrivacyState.Failed, TimeSpan.FromSeconds(5));
            Assert.Equal(CapturePrivacyState.Failed, controller.PrivacyState);
            Assert.False(controller.IsAttached);
            Assert.Equal(originalParent, Native.GetParent(qwen.Hwnd));

            // A normal explicit disable must also work after the forced fresh attachment lease.
            controller.Detach(restore: false);
            Assert.True(controller.Attach(qwen, 1.0, false));
            Assert.True(controller.EnablePrivacyHost());
            Assert.True(controller.DisablePrivacyHost());
            Assert.Equal(originalStyle, Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_STYLE).ToInt64());
            Assert.Equal(originalExStyle, Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_EXSTYLE).ToInt64());
            Assert.True(Native.GetWindowRect(qwen.Hwnd, out var restoredRect));
            Assert.Equal(originalRect.Left, restoredRect.Left);
            Assert.Equal(originalRect.Top, restoredRect.Top);
            Assert.Equal(originalRect.Right, restoredRect.Right);
            Assert.Equal(originalRect.Bottom, restoredRect.Bottom);
        }
        finally
        {
            controller.Detach(restore: true);
            controller.Dispose();
            // Preserve an unverified recovery journal for inspection/retry rather than deleting the
            // only route back from a failed real-window restoration.
            if (!File.Exists(journal))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (!condition() && timeout > TimeSpan.Zero)
            {
                timeout -= TimeSpan.FromMilliseconds(20);
                return;
            }
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void StageAndRunOptionalNativeCaptureProbes(QwenWindowController controller)
    {
        var desktopDuplication = Environment.GetEnvironmentVariable("QDC_DESKTOP_DUPLICATION_PROBE");
        var windowsGraphicsCapture = Environment.GetEnvironmentVariable("QDC_WINDOWS_GRAPHICS_CAPTURE_PROBE");
        if (string.IsNullOrWhiteSpace(desktopDuplication) && string.IsNullOrWhiteSpace(windowsGraphicsCapture)) return;
        Assert.False(string.IsNullOrWhiteSpace(desktopDuplication), "Both native capture probes must be supplied together.");
        Assert.False(string.IsNullOrWhiteSpace(windowsGraphicsCapture), "Both native capture probes must be supplied together.");
        StageHelper(desktopDuplication!, "privacy-capture-probe.exe");
        StageHelper(windowsGraphicsCapture!, "privacy-wgc-capture-probe.exe");

        var results = controller.ValidatePrivacyNativeCapturePathsAsync().GetAwaiter().GetResult();
        Assert.NotEqual(CaptureProbeVerdict.Failed, results.DesktopDuplication.Verdict);
        Assert.NotEqual(CaptureProbeVerdict.Failed, results.WindowsGraphicsCapture.Verdict);
    }

    private static void StageHelper(string source, string fileName)
    {
        Assert.True(File.Exists(source), "Configured native capture probe does not exist: " + source);
        File.Copy(source, Path.Combine(AppContext.BaseDirectory, fileName), overwrite: true);
    }
}
