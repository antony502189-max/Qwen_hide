using ChatGPTDesktopController;
using Xunit;

namespace ChatGPTDesktopController.Tests;

public sealed class CurrentAppPrivacyTests
{
    [Fact]
    public void Tray_icon_data_uses_the_Windows_Unicode_ABI_size() =>
        Assert.Equal(IntPtr.Size == 8 ? 976 : 956, System.Runtime.InteropServices.Marshal.SizeOf<TrayIconVisibilityService.NOTIFYICONDATA>());
    [Fact]
    public void Keyboard_input_uses_the_full_Windows_INPUT_union_size() =>
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());

    [Fact]
    public void Activation_thread_id_export_is_available() => Assert.NotEqual(0u, Native.GetCurrentThreadId());

    private static string PackagePath(string package, string tail = @"app\ChatGPT.exe") =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", package, tail);

    [Theory]
    [InlineData("26.1013.4894.0")]
    [InlineData("27.1.2.3")]
    public void Matches_current_package_across_app_updates(string version) =>
        Assert.True(CurrentAppPrivacyPolicy.IsPackagePath(PackagePath($"OpenAI.Codex_{version}_x64__2p2nqsd0c76g0")));

    [Theory]
    [InlineData("Other.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0")]
    [InlineData("OpenAI.Codex_26.1013.4894.0_x64__otherpublisher")]
    [InlineData("OpenAI.ChatGPT-Desktop_26.1013.4894.0_x64__2p2nqsd0c76g0")]
    public void Rejects_other_packages_and_Classic(string package) =>
        Assert.False(CurrentAppPrivacyPolicy.IsPackagePath(PackagePath(package)));

    [Fact]
    public void Rejects_same_name_elsewhere_and_non_ui_executables()
    {
        Assert.False(CurrentAppPrivacyPolicy.IsPackagePath(@"C:\Temp\OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"));
        Assert.False(CurrentAppPrivacyPolicy.IsPackagePath(PackagePath("OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0", @"app\codex.exe")));
        Assert.False(CurrentAppPrivacyPolicy.IsPackagePath(null));
    }
}
