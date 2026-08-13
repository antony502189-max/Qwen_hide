using QwenWorkOverlay;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace QwenWorkOverlay.Tests;

public sealed class NativeWindowControllerIntegrationTests
{
    [Fact]
    public void Controller_mutates_and_exactly_restores_a_real_windows_hwnd()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var ready = new ManualResetEventSlim(false);
        System.Windows.Forms.Form? form = null;
        Exception? uiError = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                var created = new System.Windows.Forms.Form
                {
                    Text = "QDC native window controller integration test",
                    Width = 640,
                    Height = 400,
                    ShowInTaskbar = false,
                    TopMost = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    Left = 80,
                    Top = 80
                };
                form = created;
                created.Shown += (_, _) => ready.Set();
                System.Windows.Forms.Application.Run(created);
            }
            catch (Exception ex)
            {
                uiError = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "QDC.NativeWindowControllerTest.UI"
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Test HWND did not become ready.");
        if (uiError is not null) throw new Xunit.Sdk.XunitException("Test UI thread failed: " + uiError);
        Assert.NotNull(form);
        var testForm = form!;
        var hwnd = testForm.Handle;

        var tempRoot = Path.Combine(Path.GetTempPath(), "QdcWindowControllerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var journal = Path.Combine(tempRoot, "recovery.json");
        var logger = new AppLogger();
        var recovery = new WindowRecoveryService(logger, journal);
        using var controller = new QwenWindowController(logger, recovery);

        var originalStyle = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
        var originalVisible = NativeTest.IsWindowVisible(hwnd);
        using var process = Process.GetCurrentProcess();
        var target = new QwenTarget(
            process.Id,
            hwnd,
            "QdcTestHost",
            process.MainModule?.FileName,
            testForm.Text,
            "WindowsForms10.Window",
            process.StartTime.ToUniversalTime().Ticks);

        try
        {
            Assert.True(controller.Attach(target, .70, true));
            Assert.True(controller.IsAttached);
            // Attach is observational: no journal or external window state is created until a
            // user explicitly requests a mutation.
            Assert.False(File.Exists(journal));
            Assert.Equal(originalStyle, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64());
            Assert.True(controller.SetOpacity(.70));
            Assert.True(controller.SetTopMost(true));
            Assert.True(File.Exists(journal));

            var styled = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            Assert.NotEqual(0, styled & NativeTest.WS_EX_LAYERED);
            Assert.NotEqual(0, styled & NativeTest.WS_EX_TOPMOST);
            Assert.True(NativeTest.GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags));
            Assert.NotEqual(0u, flags & NativeTest.LWA_ALPHA);
            Assert.InRange(alpha, (byte)176, (byte)180);

            Assert.True(controller.SetClickThrough(true));
            styled = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            Assert.NotEqual(0, styled & NativeTest.WS_EX_TRANSPARENT);

            Assert.True(controller.SetOpacity(1.0));
            styled = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            Assert.NotEqual(0, styled & NativeTest.WS_EX_LAYERED); // click-through still requires layered state

            Assert.True(controller.SetClickThrough(false));
            styled = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            // Controller TopMost is still enabled here; only the opacity/click-through bits should have returned
            // to their original values. TopMost is intentionally removed in the next assertion block.
            Assert.Equal(originalStyle | NativeTest.WS_EX_TOPMOST, styled);

            Assert.True(controller.SetTopMost(false));
            styled = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            Assert.Equal(0, styled & NativeTest.WS_EX_TOPMOST);
            Assert.Equal(originalStyle, styled);

            Assert.True(controller.ToggleVisibility());
            Assert.True(SpinWait.SpinUntil(() => !NativeTest.IsWindowVisible(hwnd), TimeSpan.FromSeconds(3)));
            Assert.True(controller.ToggleVisibility());
            Assert.True(SpinWait.SpinUntil(() => NativeTest.IsWindowVisible(hwnd), TimeSpan.FromSeconds(3)));

            controller.Detach(restore: true);
            Assert.False(controller.IsAttached);
            Assert.Equal(originalStyle, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64());
            Assert.Equal(originalVisible, NativeTest.IsWindowVisible(hwnd));
            Assert.False(File.Exists(journal));
        }
        finally
        {
            controller.Detach(restore: true);
            try { testForm.BeginInvoke((Action)testForm.Close); } catch { }
            Assert.True(uiThread.Join(TimeSpan.FromSeconds(10)), "Test HWND thread did not shut down.");
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static class NativeTest
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const uint LWA_ALPHA = 0x00000002;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);
    }
}
