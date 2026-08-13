using Xunit;

namespace QwenWorkOverlay.Tests;

/// <summary>
/// Opt-in target-machine test. It makes only reversible controller API mutations and always
/// restores from the journal in finally; CI never sets the guard variable.
/// </summary>
public sealed class RealQwenWindowMutationIntegrationTests
{
    [Fact]
    public void Real_qwen_topmost_and_clickthrough_are_reversible()
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(Environment.GetEnvironmentVariable("QDC_RUN_REAL_QWEN_WINDOW_TESTS"), "1", StringComparison.Ordinal)) return;

        using var logger = new AppLogger();
        var target = new QwenProcessLocator(logger).FindRunningTarget();
        Assert.NotNull(target);
        var qwen = target!;
        var originalExStyle = Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_EXSTYLE).ToInt64();
        var originalVisible = Native.IsWindowVisible(qwen.Hwnd);
        var root = Path.Combine(Path.GetTempPath(), "QdcRealWindowMutation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var journal = Path.Combine(root, "recovery.json");
        var recovery = new WindowRecoveryService(logger, journal);
        using var controller = new QwenWindowController(logger, recovery);

        try
        {
            Assert.True(controller.Attach(qwen));
            Assert.True(controller.SetTopMost(true));
            Assert.True(controller.SetClickThrough(true));
            var active = Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_EXSTYLE).ToInt64();
            Assert.NotEqual(0, active & Native.WS_EX_LAYERED);
            Assert.NotEqual(0, active & Native.WS_EX_TRANSPARENT);
        }
        finally
        {
            controller.Detach(restore: true);
            Assert.Equal(originalExStyle, Native.GetWindowLongPtr(qwen.Hwnd, Native.GWL_EXSTYLE).ToInt64());
            Assert.Equal(originalVisible, Native.IsWindowVisible(qwen.Hwnd));
            Assert.False(File.Exists(journal));
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
