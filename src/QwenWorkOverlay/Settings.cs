using System.Text.Json;

namespace QwenWorkOverlay;

public sealed class AppSettings
{
    public double Opacity { get; set; } = .85;
    public bool TopMost { get; set; } = true;
    public bool AutoLaunchQwen { get; set; } = true;
    public bool StartControllerInTray { get; set; } = true;
    public string? QwenExecutablePath { get; set; }

    public string? MicrophoneDeviceId { get; set; }
    public string? LoopbackDeviceId { get; set; }
    // Render side of an optional pre-installed virtual cable. Its paired capture endpoint can be assigned to Qwen only.
    public string? VirtualMixOutputDeviceId { get; set; }
    public float MicGain { get; set; } = 1f;
    public float SystemGain { get; set; } = 1f;
    public bool RightCtrlAudioEnabled { get; set; } = true;

    public double ControllerX { get; set; } = 120;
    public double ControllerY { get; set; } = 120;
}

public sealed class SettingsService
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenDesktopController");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(Root);
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }

        Current.Opacity = Math.Clamp(Current.Opacity, .35, 1.0);
        Current.MicGain = Math.Clamp(Current.MicGain, 0f, 4f);
        Current.SystemGain = Math.Clamp(Current.SystemGain, 0f, 4f);
    }

    public void Save()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class WindowStateNormalizer
{
    public static (double X, double Y) Normalize(double x, double y, double width, double height, IEnumerable<System.Windows.Forms.Screen> screens)
    {
        var array = screens.ToArray();
        if (array.Length == 0) return (x, y);
        var area = array.Select(s => s.WorkingArea).FirstOrDefault(a => x < a.Right && x + width > a.Left && y < a.Bottom && y + height > a.Top);
        if (area == default) area = array[0].WorkingArea;
        return (Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }
}
