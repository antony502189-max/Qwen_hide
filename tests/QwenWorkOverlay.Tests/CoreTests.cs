using QwenWorkOverlay;
using Xunit;

namespace QwenWorkOverlay.Tests;
public class CoreTests
{
    [Fact] public void Settings_round_trip() { var json=System.Text.Json.JsonSerializer.Serialize(new AppSettings{Opacity=.72,TopMost=false,MicGain=1.3f});var value=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;Assert.Equal(.72,value.Opacity);Assert.False(value.TopMost);Assert.Equal(1.3f,value.MicGain); }
    [Fact] public void Mixer_limits_clipping() { var x=AudioMixer.Mix([1f,1f], [1f,-3f],1,1); Assert.All(x,v=>Assert.InRange(v,-1f,1f)); Assert.True(x[0]>.9f); }
    [Fact] public void Mixer_honours_gain() { var x=AudioMixer.Mix([.5f], [.5f],.5f,0); Assert.InRange(x[0],.24f,.25f); }
    [Fact] public void Window_state_returns_to_visible_desktop() { var s=new[]{System.Windows.Forms.Screen.PrimaryScreen!}; var p=WindowStateNormalizer.Normalize(500000,500000,900,600,s); Assert.True(p.X<s[0].WorkingArea.Right); Assert.True(p.Y<s[0].WorkingArea.Bottom); }
    [Fact] public void Default_guard_detects_unchanged_snapshot() { using var d=new AudioDeviceService(); var guard=new AudioDefaultDeviceGuard(d); Assert.True(guard.Verify(d)); }
    [Fact] public void Audio_conversion_is_bounded() { Assert.Equal(-1f,AudioFormatConverter.Pcm16ToFloat(short.MinValue));Assert.Equal(short.MaxValue,AudioFormatConverter.FloatToPcm16(4)); }
    [Fact] public void Audio_conversion_downmixes_stereo() { var format=new NAudio.Wave.WaveFormat(48000,16,2); var bytes=new byte[4];BitConverter.GetBytes((short)32767).CopyTo(bytes,0);BitConverter.GetBytes((short)-32768).CopyTo(bytes,2);var sample=AudioFormatConverter.ToMonoFloat48k(bytes,bytes.Length,format);Assert.Single(sample);Assert.InRange(sample[0],-.01f,.01f); }
    [Fact] public void Right_ctrl_repeats_do_not_restart() { var s=new RightCtrlStateMachine();Assert.True(s.OnDown());Assert.False(s.OnDown());Assert.True(s.OnUp());Assert.False(s.OnUp()); }
    [Fact] public void Clipboard_prefers_exact_text() { Assert.Equal(ClipboardPayloadKind.Text,ClipboardPolicy.Classify(true,true)); Assert.Equal(ClipboardPayloadKind.Image,ClipboardPolicy.Classify(false,true)); }
    [Fact] public void Capture_protection_never_claims_on_after_failure() { Assert.Equal("FAILED",new CaptureProtectionResult(true,false).Status);Assert.Equal("OFF",new CaptureProtectionResult(false,true).Status); }
    [Fact] public void Virtual_mix_policy_rejects_physical_speakers() { Assert.True(VirtualMixOutputPolicy.IsRecognizedVirtualName("CABLE Input (VB-Audio Virtual Cable)"));Assert.False(VirtualMixOutputPolicy.IsRecognizedVirtualName("Speakers (Realtek(R) Audio)")); }
}
