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
            OriginalColorKey = 42
        };
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        var restored = System.Text.Json.JsonSerializer.Deserialize<WindowRecoverySnapshot>(json)!;
        Assert.Equal(snapshot.ProcessId, restored.ProcessId);
        Assert.Equal(snapshot.ProcessStartUtcTicks, restored.ProcessStartUtcTicks);
        Assert.Equal(snapshot.Hwnd, restored.Hwnd);
        Assert.Equal(snapshot.OriginalExStyle, restored.OriginalExStyle);
        Assert.Equal(snapshot.OriginalAlpha, restored.OriginalAlpha);
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
    public void Virtual_mix_policy_rejects_physical_speakers_and_accepts_known_virtual_devices()
    {
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("CABLE Input (VB-Audio Virtual Cable)"));
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("VoiceMeeter Input"));
        Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("VB-Audio Point"));
        Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("Speakers (Realtek(R) Audio)"));
    }
}
