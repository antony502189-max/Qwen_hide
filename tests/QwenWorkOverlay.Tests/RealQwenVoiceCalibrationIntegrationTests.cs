using Xunit;

namespace QwenWorkOverlay.Tests;

/// <summary>
/// Opt-in target-machine check. It validates only the calibrated point's current ownership; it
/// does not post any mouse message, start voice recording, or move the physical cursor.
/// </summary>
public sealed class RealQwenVoiceCalibrationIntegrationTests
{
    [Fact]
    public void Real_qwen_calibrated_voice_point_is_safe_to_target_without_clicking()
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(Environment.GetEnvironmentVariable("QDC_RUN_REAL_QWEN_VOICE_VALIDATION"), "1", StringComparison.Ordinal)) return;

        using var logger = new AppLogger();
        var target = new QwenProcessLocator(logger).FindRunningTarget();
        Assert.NotNull(target);
        var fallback = new QwenVoiceClickFallback(logger);
        Assert.True(fallback.HasCalibration);
        Assert.True(fallback.TryValidate(target!.Hwnd, out var diagnostic), diagnostic);
        Assert.Contains("verified", diagnostic, StringComparison.OrdinalIgnoreCase);
    }
}
