using System.Text.Json;

namespace ChatGPTDesktopController;

public sealed class ControllerSettings
{
    public string? ExecutablePath { get; set; }
    public bool StartInTray { get; set; }
    public bool AutoLaunchTarget { get; set; }
    public double Opacity { get; set; } = 1;
    public string VoiceHotkey { get; set; } = "Ctrl+Shift+R";
    public bool RightCtrlAudioEnabled { get; set; }
    public string? PhysicalMicrophoneId { get; set; }
    public string? VirtualOutputId { get; set; }
    public double MicrophoneGain { get; set; } = 1;
    public double SystemAudioGain { get; set; } = 1;
}

public static class SettingsService
{
    public static ControllerSettings Load()
    {
        try { return JsonSerializer.Deserialize<ControllerSettings>(File.ReadAllText(AppPaths.Settings)) ?? new(); }
        catch { return new(); }
    }
    public static void Save(ControllerSettings settings)
    {
        AppPaths.Ensure();
        var tmp = AppPaths.Settings + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, AppPaths.Settings, true);
    }
}
