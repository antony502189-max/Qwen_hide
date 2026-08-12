using QwenWorkOverlay;

namespace QwenWorkOverlay.Tests;

public sealed class VoiceClickFallbackTests
{
    [Fact]
    public void ComputeClientPoint_AnchorsFromBottomRight()
    {
        var point = QwenVoiceClickFallback.ComputeClientPoint(1200, 800, 150, 70);
        Assert.Equal(1050, point.X);
        Assert.Equal(730, point.Y);
    }

    [Fact]
    public void ComputeClientPoint_TracksResizeByKeepingOffsets()
    {
        var before = QwenVoiceClickFallback.ComputeClientPoint(1200, 800, 120, 55);
        var after = QwenVoiceClickFallback.ComputeClientPoint(1600, 1000, 120, 55);

        Assert.Equal(1080, before.X);
        Assert.Equal(745, before.Y);
        Assert.Equal(1480, after.X);
        Assert.Equal(945, after.Y);
    }
}
