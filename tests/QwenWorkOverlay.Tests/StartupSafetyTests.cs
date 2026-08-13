using QwenWorkOverlay;
using Xunit;

namespace QwenWorkOverlay.Tests;

public sealed class StartupSafetyTests
{
    [Fact]
    public void NewSettings_StartOpaqueToAvoidAutomaticLayeredChromiumWindow()
    {
        var settings = new AppSettings();
        Assert.Equal(2, settings.SettingsVersion);
        Assert.Equal(1.0, settings.Opacity, 3);
    }
}
