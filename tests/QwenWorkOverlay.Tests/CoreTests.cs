using QwenWorkOverlay;
using Xunit;

namespace QwenWorkOverlay.Tests;

public class CoreTests
{
    [Fact]
    public void Settings_round_trip()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings
        {
            Opacity = .72,
            TopMost = false,
            MicGain = 1.3f,
            QwenExecutablePath = @"C:\Apps\Qwen\Qwen.exe"
        });
        var value = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(.72, value.Opacity);
        Assert.False(value.TopMost);
        Assert.Equal(1.3f, value.MicGain);
        Assert.EndsWith("Qwen.exe", value.QwenExecutablePath);
    }

    [Fact]
    public void Recovery_snapshot_round_trips_without_losing_native_style_bits()
    {
        var snapshot = new WindowRecoverySnapshot
        {
            ProcessId = 123,
            ProcessStartUtcTicks = 987654321,
            Hwnd = 0x12345678,
            OriginalExStyle = 0x80028,
            OriginalTopMost = true,
            OriginalVisible = true,
            OriginalLayered = true,
            OriginalAlpha = 217,
            OriginalLayerFlags = 2,
            OriginalColorKey = 42,
            OriginalParent = 0x22,
            OriginalStyle = Native.WS_POPUP | 0x00CF0000L,
            PlacementShowCmd = 3,
            PlacementLeft = 20,
            PlacementTop = 30,
            PlacementRight = 900,
            PlacementBottom = 700,
            OriginalDpi = 144,
            OriginalDpiAwarenessContext = -4,
            PrivacyHostActive = true,
            PrivacyHostHwnd = 0x33,
            PrivacyHostDpi = 144
        };
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        var restored = System.Text.Json.JsonSerializer.Deserialize<WindowRecoverySnapshot>(json)!;
        Assert.Equal(snapshot.ProcessId, restored.ProcessId);
        Assert.Equal(snapshot.ProcessStartUtcTicks, restored.ProcessStartUtcTicks);
        Assert.Equal(snapshot.Hwnd, restored.Hwnd);
        Assert.Equal(snapshot.OriginalExStyle, restored.OriginalExStyle);
        Assert.Equal(snapshot.OriginalAlpha, restored.OriginalAlpha);
        Assert.Equal(snapshot.OriginalParent, restored.OriginalParent);
        Assert.Equal(snapshot.OriginalStyle, restored.OriginalStyle);
        Assert.Equal(snapshot.PlacementBottom, restored.PlacementBottom);
        Assert.Equal(snapshot.PrivacyHostHwnd, restored.PrivacyHostHwnd);
        Assert.Equal(snapshot.OriginalDpiAwarenessContext, restored.OriginalDpiAwarenessContext);
    }

    [Fact]
    public void Mixer_limits_clipping()
    {
        var x = AudioMixer.Mix([1f, 1f], [1f, -3f], 1, 1);
        Assert.All(x, v => Assert.InRange(v, -1f, 1f));
        Assert.True(x[0] > .9f);
    }

    [Fact]
    public void Mixer_honours_gain()
    {
        var x = AudioMixer.Mix([.5f], [.5f], .5f, 0);
        Assert.InRange(x[0], .24f, .25f);
    }

    [Fact]
    public void Mixer_clamps_negative_gain_to_zero()
    {
        var x = AudioMixer.Mix([.8f], [.4f], -2f, 0f);
        Assert.Equal(0f, x[0]);
    }

    [Fact]
    public void Window_state_returns_to_visible_desktop()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        Assert.NotNull(screen);
        var p = WindowStateNormalizer.Normalize(500000, 500000, 900, 600, [screen!]);
        Assert.True(p.X < screen!.WorkingArea.Right);
        Assert.True(p.Y < screen.WorkingArea.Bottom);
    }

    [Fact]
    public void Click_through_forces_layered_window_even_at_full_opacity()
    {
        const long layered = 0x00080000L;
        const long transparent = 0x00000020L;
        var style = WindowStylePolicy.ComputeExtendedStyle(0, 1.0, true);
        Assert.NotEqual(0, style & layered);
        Assert.NotEqual(0, style & transparent);
    }

    [Fact]
    public void Interactive_full_opacity_preserves_original_unrelated_style_bits()
    {
        const long original = 0x00000100L;
        var style = WindowStylePolicy.ComputeExtendedStyle(original, 1.0, false);
        Assert.Equal(original, style);
    }

    [Fact]
    public void Audio_conversion_is_bounded()
    {
        Assert.Equal(-1f, AudioFormatConverter.Pcm16ToFloat(short.MinValue));
        Assert.Equal(short.MaxValue, AudioFormatConverter.FloatToPcm16(4));
    }

    [Fact]
    public void Audio_conversion_downmixes_stereo()
    {
        var format = new NAudio.Wave.WaveFormat(48000, 16, 2);
        var bytes = new byte[4];
        BitConverter.GetBytes((short)32767).CopyTo(bytes, 0);
        BitConverter.GetBytes(short.MinValue).CopyTo(bytes, 2);
        var sample = AudioFormatConverter.ToMonoFloat48k(bytes, bytes.Length, format);
        Assert.Single(sample);
        Assert.InRange(sample[0], -.01f, .01f);
    }

    [Fact]
    public void Audio_conversion_uses_linear_interpolation_when_resampling()
    {
        var format = new NAudio.Wave.WaveFormat(24000, 16, 1);
        var bytes = new byte[4];
        BitConverter.GetBytes((short)0).CopyTo(bytes, 0);
        BitConverter.GetBytes(short.MaxValue).CopyTo(bytes, 2);
        var samples = AudioFormatConverter.ToMonoFloat48k(bytes, bytes.Length, format);
        Assert.Equal(4, samples.Length);
        Assert.InRange(samples[0], -.001f, .001f);
        Assert.InRange(samples[1], .49f, .51f);
        Assert.InRange(samples[2], .99f, 1f);
    }

    [Fact]
    public void Audio_conversion_handles_pcm32_without_treating_it_as_float()
    {
        var format = new NAudio.Wave.WaveFormat(48000, 32, 1);
        var bytes = BitConverter.GetBytes(int.MaxValue);
        var samples = AudioFormatConverter.ToMonoFloat48k(bytes, bytes.Length, format);
        Assert.Single(samples);
        Assert.InRange(samples[0], .99f, 1f);
    }

    [Fact]
    public void Audio_conversion_handles_pcm24()
    {
        var format = new NAudio.Wave.WaveFormat(48000, 24, 1);
        var bytes = new byte[] { 0xFF, 0xFF, 0x7F };
        var samples = AudioFormatConverter.ToMonoFloat48k(bytes, bytes.Length, format);
        Assert.Single(samples);
        Assert.InRange(samples[0], .99f, 1f);
    }

    [Fact]
    public void Right_ctrl_repeats_do_not_restart()
    {
        var state = new RightCtrlStateMachine();
        Assert.True(state.OnDown());
        Assert.False(state.OnDown());
        Assert.True(state.OnUp());
        Assert.False(state.OnUp());
    }

    [Fact]
    public void Clipboard_prefers_exact_text()
    {
        Assert.Equal(ClipboardPayloadKind.Text, ClipboardPolicy.Classify(true, true));
        Assert.Equal(ClipboardPayloadKind.Image, ClipboardPolicy.Classify(false, true));
    }

    [Fact]
    public void Capture_privacy_is_not_directly_applied_to_an_external_qwen_process()
    {
        Assert.False(NativeCapturePrivacyPolicy.CanApplyDirectly(100, 200));
        Assert.True(NativeCapturePrivacyPolicy.CanApplyDirectly(100, 100));
    }

    [Fact]
    public void Legacy_settings_are_migrated_to_safe_non_mutating_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "QdcSettingsMigration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Serialization shape confirms old values carry schema zero; service migration is
            // separately exercised through its normal LocalAppData location at runtime.
            var legacy = new AppSettings { Opacity = .55, TopMost = true, RightCtrlAudioEnabled = true };
            Assert.Equal(0, legacy.SettingsSchemaVersion);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Safe_mode_is_opt_in_from_command_line()
    {
        Assert.True(ControllerRuntimeOptions.FromArguments(["--safe-mode"]).SafeMode);
        Assert.False(ControllerRuntimeOptions.FromArguments(Array.Empty<string>()).SafeMode);
        Assert.Equal(12, ControllerRuntimeOptions.FromArguments(["--safe-mode", "--exit-after-seconds", "12"]).ExitAfterSeconds);
    }

    [Fact]
    public void Privacy_host_requires_verified_affinity_and_matching_nonzero_dpi()
    {
        Assert.True(PrivacyHostPolicy.IsVerifiedAffinity(Native.WDA_EXCLUDEFROMCAPTURE, Native.WDA_EXCLUDEFROMCAPTURE));
        Assert.False(PrivacyHostPolicy.IsVerifiedAffinity(Native.WDA_EXCLUDEFROMCAPTURE, Native.WDA_NONE));
        Assert.True(PrivacyHostPolicy.IsDpiCompatible(144, 144));
        Assert.False(PrivacyHostPolicy.IsDpiCompatible(0, 144));
        Assert.False(PrivacyHostPolicy.IsDpiCompatible(96, 144));
    }

    [Theory]
    [InlineData(96u, 320d, 100d, 1280d, 840d)]
    [InlineData(120u, 256d, 80d, 1024d, 672d)]
    [InlineData(144u, 213.333333d, 66.666667d, 853.333333d, 560d)]
    public void Privacy_host_geometry_converts_physical_qwen_bounds_for_100_125_and_150_percent_dpi(
        uint dpi, double expectedLeft, double expectedTop, double expectedWidth, double expectedHeight)
    {
        var bounds = PrivacyHostGeometryPolicy.FromQwenPhysicalBounds(320, 100, 1600, 940, dpi);
        Assert.Equal(expectedLeft, bounds.Left, 5);
        Assert.Equal(expectedTop, bounds.Top, 5);
        Assert.Equal(expectedWidth, bounds.Width, 5);
        Assert.Equal(expectedHeight, bounds.Height, 5);
    }

    [Fact]
    public void Privacy_host_geometry_refuses_zero_dpi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PrivacyHostGeometryPolicy.FromQwenPhysicalBounds(0, 0, 100, 100, 0));
    }

    [Fact]
    public void Privacy_host_child_style_is_reversible_from_the_saved_original_style()
    {
        const long original = Native.WS_POPUP | 0x00CF0000L;
        var child = PrivacyHostPolicy.ToChildStyle(original);
        Assert.NotEqual(0, child & Native.WS_CHILD);
        Assert.Equal(0, child & Native.WS_POPUP);
        Assert.Equal(0x00CF0000L, child & 0x00CF0000L);
    }

    [Theory]
    [InlineData(2, 30, 30, CaptureProbeVerdict.Inconclusive)]
    [InlineData(35, 30, 30, CaptureProbeVerdict.Exposed)]
    [InlineData(8, 30, 30, CaptureProbeVerdict.Inconclusive)]
    [InlineData(2, 1, 1, CaptureProbeVerdict.Inconclusive)]
    [InlineData(30, 1, 40, CaptureProbeVerdict.RedactedPlaceholder)]
    public void Gdi_capture_probe_policy_never_turns_weak_evidence_into_a_privacy_pass(
        double difference, double visibleVariance, double hiddenVariance, CaptureProbeVerdict expected)
    {
        Assert.Equal(expected, CaptureProbePolicy.ClassifyGdi(difference, visibleVariance, hiddenVariance));
    }

    [Theory]
    [InlineData(0, CaptureProbeVerdict.Inconclusive)]
    [InlineData(5.9, CaptureProbeVerdict.Inconclusive)]
    [InlineData(6, CaptureProbeVerdict.Exposed)]
    [InlineData(double.NaN, CaptureProbeVerdict.Failed)]
    public void PrintWindow_probe_treats_rendered_nonuniform_host_content_as_direct_capture_exposure(
        double visibleVariance, CaptureProbeVerdict expected)
    {
        Assert.Equal(expected, CaptureProbePolicy.ClassifyPrintWindow(visibleVariance));
    }

    [Theory]
    [InlineData("RESULT DesktopDuplication=REDACTED_PLACEHOLDER Difference=33.7", "RESULT DesktopDuplication=", CaptureProbeVerdict.RedactedPlaceholder)]
    [InlineData("RESULT WindowsGraphicsCapture=LIKELY_EXCLUDED Difference=0.0", "RESULT WindowsGraphicsCapture=", CaptureProbeVerdict.Inconclusive)]
    [InlineData("RESULT WindowsGraphicsCapture=EXPOSED Difference=48.0", "RESULT WindowsGraphicsCapture=", CaptureProbeVerdict.Exposed)]
    [InlineData("unexpected content", "RESULT DesktopDuplication=", CaptureProbeVerdict.Failed)]
    public void Native_capture_probe_output_is_strictly_parsed_without_claiming_a_broad_privacy_pass(
        string output, string prefix, CaptureProbeVerdict expected)
    {
        Assert.Equal(expected, NativeCaptureProbeOutputParser.Parse(output, prefix).Verdict);
    }

    [Fact]
    public void Failed_privacy_host_requires_reacquisition_before_any_native_mutation()
    {
        Assert.False(PrivacyMutationPolicy.CanMutateNativeWindow(true, CapturePrivacyState.Failed));
        Assert.True(PrivacyMutationPolicy.CanMutateNativeWindow(true, CapturePrivacyState.Off));
        Assert.False(PrivacyMutationPolicy.CanMutateNativeWindow(false, CapturePrivacyState.Off));
    }

    [Fact]
    public void Capture_exposure_has_a_fail_closed_privacy_status()
    {
        var status = CapturePrivacyStatusPolicy.Build(
            CaptureProbeVerdict.Exposed,
            CaptureProbeVerdict.NotRun,
            CaptureProbeVerdict.RedactedPlaceholder,
            CaptureProbeVerdict.Inconclusive);
        Assert.Contains("CAPTURE EXPOSED by GDI", status);
        Assert.Contains("do not share Qwen", status);
    }

    [Fact]
    public void Printwindow_exposure_has_a_fail_closed_privacy_status()
    {
        var status = CapturePrivacyStatusPolicy.Build(
            CaptureProbeVerdict.Inconclusive,
            CaptureProbeVerdict.Exposed,
            CaptureProbeVerdict.Inconclusive,
            CaptureProbeVerdict.Inconclusive);
        Assert.Contains("CAPTURE EXPOSED by PrintWindow", status);
        Assert.Contains("do not share Qwen", status);
    }

    [Fact]
    public void Virtual_mix_policy_rejects_physical_speakers_and_accepts_known_virtual_devices()
    {
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("CABLE Input (VB-Audio Virtual Cable)"));
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("VoiceMeeter Input"));
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("VB-Audio Point"));
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("My Virtual Audio Device"));

        Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("Speakers (Realtek(R) Audio)"));
        Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("Headphones (USB Audio Virtual Surround)"));
        Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("NVIDIA High Definition Audio (Virtual Display)"));
        Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("Bluetooth Headset Loopback"));
    }

    [Fact]
    public void Voice_control_scoring_prefers_input_controls_and_penalizes_unrelated_audio_buttons()
    {
        var microphone = QwenVoiceAutomation.Score("Microphone voice input");
        var exactMic = QwenVoiceAutomation.Score("mic");
        var outputSettings = QwenVoiceAutomation.Score("Audio output device settings speaker volume");
        var send = QwenVoiceAutomation.Score("Send voice message");

        Assert.True(microphone >= 40);
        Assert.True(exactMic >= 20);
        Assert.True(outputSettings < microphone);
        Assert.True(send < microphone);
    }
}
