using QwenWorkOverlay;
using Xunit;

namespace QwenWorkOverlay.Tests;

public sealed class VoiceClickFallbackTests
{
    [Fact]
    public void ComputeScreenPoint_AnchorsFromWindowBottomRight()
    {
        var point = QwenVoiceClickFallback.ComputeScreenPoint(100, 50, 1300, 850, 150, 70);
        Assert.Equal(1150, point.X);
        Assert.Equal(780, point.Y);
    }

    [Fact]
    public void ComputeScreenPoint_TracksResizeByKeepingOffsets()
    {
        var before = QwenVoiceClickFallback.ComputeScreenPoint(100, 50, 1300, 850, 120, 55);
        var after = QwenVoiceClickFallback.ComputeScreenPoint(100, 50, 1700, 1050, 120, 55);

        Assert.Equal(1180, before.X);
        Assert.Equal(795, before.Y);
        Assert.Equal(1580, after.X);
        Assert.Equal(995, after.Y);
    }
}
