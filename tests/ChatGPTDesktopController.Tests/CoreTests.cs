using ChatGPTDesktopController;
using Xunit;

namespace ChatGPTDesktopController.Tests;

public sealed class CoreTests
{
    [Theory] [InlineData(.01, .35)] [InlineData(.35, .35)] [InlineData(.62, .62)] [InlineData(1.5, 1)]
    public void Opacity_is_safe(double input, double expected) => Assert.Equal(expected, OpacityPolicy.Clamp(input));
    [Fact] public void Modifier_release_requires_all_keys_up() { Assert.False(ModifierReleasePolicy.Released(true, false, false)); Assert.False(ModifierReleasePolicy.Released(false, true, false)); Assert.True(ModifierReleasePolicy.Released(false, false, false)); }
    [Fact] public void Right_ctrl_state_is_edge_triggered() { var state = new RightCtrlStateMachine(); Assert.True(state.OnDown()); Assert.False(state.OnDown()); Assert.True(state.OnUp()); Assert.False(state.OnUp()); }
    [Fact] public void Audio_fails_closed_without_dedicated_virtual_endpoint() { Assert.False(AudioEndpointSafety.CanStart("mic", null, true)); Assert.False(AudioEndpointSafety.CanStart("same", "same", true)); Assert.False(AudioEndpointSafety.CanStart("mic", "virtual", false)); Assert.True(AudioEndpointSafety.CanStart("mic", "virtual", true)); }
    [Theory] [InlineData("CABLE Input (VB-Audio Virtual Cable)")] [InlineData("VoiceMeeter Input")]
    public void Recognized_virtual_outputs_are_allowed_by_name_policy(string name) => Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName(name));
    [Theory] [InlineData("Speakers (Realtek)")] [InlineData("Headphones")]
    public void Physical_outputs_are_rejected_by_virtual_output_policy(string name) => Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName(name));
    [Fact] public void Voice_never_invokes_an_undiscovered_shortcut() { Assert.False(VoiceShortcutPolicy.CanInvoke(false, "Alt+Space")); Assert.False(VoiceShortcutPolicy.CanInvoke(true, "")); Assert.True(VoiceShortcutPolicy.CanInvoke(true, "Alt+Space")); }
    [Theory] [InlineData("Запустить голосовой режим")] [InlineData("Начало диктовки")] [InlineData("Start voice mode")]
    public void Voice_accessibility_fallback_is_localized_and_not_coordinate_based(string name) => Assert.True(VoiceControlPolicy.NameLooksLikeVoiceControl(name));
    [Fact] public void Target_validation_rejects_unrelated_processes() { Assert.False(ChatGPTProcessLocator.IsChatGPTClassicExecutable("C:\\Windows\\notepad.exe")); Assert.False(ChatGPTProcessLocator.IsChatGPTClassicExecutable(null)); }
    [Fact] public void Default_settings_are_conservative() { var s = new ControllerSettings(); Assert.Equal(1, s.Opacity); Assert.False(s.AutoLaunchTarget); Assert.False(s.RightCtrlAudioEnabled); }
    [Fact] public void Window_style_computation_preserves_base_style_and_adds_only_controller_bits() { const long original = 0x100L; var regular = WindowStylePolicy.ComposeVisualStyle(original, false); var transparent = WindowStylePolicy.ComposeVisualStyle(original, true); Assert.Equal(original | 0x80000L, regular); Assert.Equal(regular | 0x20L, transparent); }
    [Fact] public void Stale_journal_for_nonexistent_window_is_removed_safely()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try { File.WriteAllText(path, "{\"Hwnd\":123,\"ProcessId\":999999}"); var recovery = new RecoveryService(new AppLogger(), path); Assert.False(recovery.TryRecoverStaleState()); Assert.False(File.Exists(path)); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact] public async Task Paste_without_a_target_reports_a_resolved_failure_stage()
    {
        var result = await new ComposerAutomation(new AppLogger()).PasteImageAsync(null, new WindowController(new AppLogger(), new RecoveryService(new AppLogger())));
        Assert.Equal(PasteStage.Failed, result.Stage); Assert.Contains("not attached", result.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
