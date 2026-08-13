using QwenWorkOverlay;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace QwenWorkOverlay.Tests;

public sealed class NativeRecoveryIntegrationTests
{
    [Fact]
    public void Recovery_service_restores_a_real_windows_hwnd_after_simulated_crash_state()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var ready = new ManualResetEventSlim(false);
        System.Windows.Forms.Form? form = null;
        Exception? uiError = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                var createdForm = new System.Windows.Forms.Form
                {
                    Text = "QDC native recovery integration test",
                    Width = 520,
                    Height = 320,
                    ShowInTaskbar = false,
                    StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    Left = 50,
                    Top = 50
                };
                form = createdForm;
                createdForm.Shown += (_, _) => ready.Set();
                System.Windows.Forms.Application.Run(createdForm);
            }
            catch (Exception ex)
            {
                uiError = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "QDC.NativeRecoveryTest.UI"
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Test HWND did not become ready.");
        if (uiError is not null) throw new Xunit.Sdk.XunitException("Test UI thread failed: " + uiError);
        Assert.NotNull(form);
        var testForm = form!;
        var hwnd = testForm.Handle;
        Assert.NotEqual(IntPtr.Zero, hwnd);

        var tempRoot = Path.Combine(Path.GetTempPath(), "QdcRecoveryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var journal = Path.Combine(tempRoot, "recovery.json");
        var logger = new AppLogger();
        var recovery = new WindowRecoveryService(logger, journal);

        try
        {
            var original = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
            using var process = Process.GetCurrentProcess();
            var target = new QwenTarget(
                process.Id,
                hwnd,
                "QdcTestHost",
                process.MainModule?.FileName,
                testForm.Text,
                "WindowsForms10.Window",
                process.StartTime.ToUniversalTime().Ticks);

            NativeTest.ShowWindow(hwnd, NativeTest.SW_SHOWMAXIMIZED);
            Assert.True(SpinWait.SpinUntil(() => NativeTest.IsZoomed(hwnd), TimeSpan.FromSeconds(3)), "Test HWND did not maximize.");
            recovery.Save(target, new IntPtr(original), false, true, false, 255, NativeTest.LWA_ALPHA, 0);
            Assert.True(File.Exists(journal));

            var mutated = original | NativeTest.WS_EX_LAYERED | NativeTest.WS_EX_TRANSPARENT | NativeTest.WS_EX_TOPMOST;
            NativeTest.SetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE, new IntPtr(mutated));
            Assert.True(NativeTest.SetLayeredWindowAttributes(hwnd, 0, 102, NativeTest.LWA_ALPHA));
            Assert.True(NativeTest.SetWindowPos(hwnd, NativeTest.HWND_TOPMOST, 0, 0, 0, 0,
                NativeTest.SWP_NOMOVE | NativeTest.SWP_NOSIZE | NativeTest.SWP_NOACTIVATE | NativeTest.SWP_FRAMECHANGED));
            NativeTest.ShowWindow(hwnd, NativeTest.SW_HIDE);

            Assert.NotEqual(original, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64());
            Assert.False(NativeTest.IsWindowVisible(hwnd));

            Assert.True(recovery.TryRecoverStaleState());
            Assert.Equal(original, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64());
            Assert.True(NativeTest.IsWindowVisible(hwnd));
            Assert.True(NativeTest.IsZoomed(hwnd));
            Assert.False(File.Exists(journal));
        }
        finally
        {
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
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const int SW_HIDE = 0;
        public const int SW_SHOWMAXIMIZED = 3;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hwnd);
    }
}
