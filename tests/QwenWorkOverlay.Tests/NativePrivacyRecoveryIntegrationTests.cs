using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace QwenWorkOverlay.Tests;

public sealed class NativePrivacyRecoveryIntegrationTests
{
    [Fact]
    public void Recovery_restores_parent_popup_style_and_placement_after_simulated_privacy_host_failure()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var ready = new ManualResetEventSlim(false);
        System.Windows.Forms.Form? qwen = null;
        System.Windows.Forms.Form? host = null;
        var ui = new Thread(() =>
        {
            qwen = new System.Windows.Forms.Form { Text = "QDC privacy recovery child", Width = 640, Height = 400, Left = 130, Top = 90, ShowInTaskbar = false };
            host = new System.Windows.Forms.Form { Text = "QDC privacy recovery host", Width = 700, Height = 480, Left = 80, Top = 60, ShowInTaskbar = false };
            qwen.Shown += (_, _) => { host.Show(); ready.Set(); };
            System.Windows.Forms.Application.Run(qwen);
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(qwen);
        Assert.NotNull(host);

        var child = qwen!;
        var hostWindow = host!;
        var hwnd = child.Handle;
        var originalParent = NativeTest.GetParent(hwnd);
        var originalStyle = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_STYLE).ToInt64();
        var originalExStyle = NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64();
        Assert.True(NativeTest.GetWindowRect(hwnd, out var originalRect));
        var tempRoot = Path.Combine(Path.GetTempPath(), "QdcPrivacyRecovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var journal = Path.Combine(tempRoot, "recovery.json");
        var logger = new AppLogger();
        var recovery = new WindowRecoveryService(logger, journal);
        using var process = Process.GetCurrentProcess();
        var target = new QwenTarget(process.Id, hwnd, "QdcTest", process.MainModule?.FileName, child.Text, "WindowsForms10.Window", process.StartTime.ToUniversalTime().Ticks);

        try
        {
            Assert.True(recovery.Save(target, new IntPtr(originalExStyle), false, true, false, 255, NativeTest.LWA_ALPHA, 0));
            var childStyle = PrivacyHostPolicy.ToChildStyle(originalStyle);
            NativeTest.SetWindowLongPtr(hwnd, NativeTest.GWL_STYLE, new IntPtr(childStyle));
            NativeTest.SetParent(hwnd, hostWindow.Handle);
            Assert.Equal(hostWindow.Handle, NativeTest.GetParent(hwnd));

            Assert.True(recovery.TryRecoverStaleState());
            Assert.Equal(originalParent, NativeTest.GetParent(hwnd));
            Assert.Equal(originalStyle, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_STYLE).ToInt64());
            Assert.Equal(originalExStyle, NativeTest.GetWindowLongPtr(hwnd, NativeTest.GWL_EXSTYLE).ToInt64());
            Assert.True(NativeTest.GetWindowRect(hwnd, out var restoredRect));
            Assert.Equal(originalRect.Left, restoredRect.Left);
            Assert.Equal(originalRect.Top, restoredRect.Top);
            Assert.Equal(originalRect.Right, restoredRect.Right);
            Assert.Equal(originalRect.Bottom, restoredRect.Bottom);
            Assert.False(File.Exists(journal));
        }
        finally
        {
            try { hostWindow.BeginInvoke((Action)hostWindow.Close); } catch { }
            try { child.BeginInvoke((Action)child.Close); } catch { }
            Assert.True(ui.Join(TimeSpan.FromSeconds(10)));
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static class NativeTest
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CHILD = 0x40000000L;
        public const long WS_POPUP = unchecked((long)0x80000000L);
        public const uint LWA_ALPHA = 2;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        public static IntPtr GetWindowLongPtr(IntPtr h, int i) => GetWindowLongPtr64(h, i);
        public static IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v) => SetWindowLongPtr64(h, i, v);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
