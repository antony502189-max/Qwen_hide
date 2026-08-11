using System.Text.Json;

namespace QwenWorkOverlay;
public sealed class AppSettings
{
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 900;
    public double Height { get; set; } = 650;
    public string? MonitorDeviceName { get; set; }
    public double Opacity { get; set; } = .85;
    public bool TopMost { get; set; } = true;
    public bool CaptureProtection { get; set; } = true;
    public string? MicrophoneDeviceId { get; set; }
    public string? LoopbackDeviceId { get; set; }
    // Optional render side of a pre-installed virtual cable. The paired capture side is selected in Qwen's own voice UI.
    public string? VirtualMixOutputDeviceId { get; set; }
    public float MicGain { get; set; } = 1f;
    public float SystemGain { get; set; } = 1f;
    public bool RightCtrlAudioEnabled { get; set; } = true;
}
public sealed class SettingsService
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenWorkOverlay");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public AppSettings Current { get; private set; } = new();
    public void Load()
    {
        Directory.CreateDirectory(Root);
        try { if (File.Exists(SettingsPath)) Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch { Current = new(); }
        Current.Opacity = Math.Clamp(Current.Opacity, .35, 1);
        Current.Width = Math.Clamp(Current.Width, 420, 5000); Current.Height = Math.Clamp(Current.Height, 280, 5000);
    }
    public void Save() { Directory.CreateDirectory(Root); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true })); }
}
public static class WindowStateNormalizer
{
    public static (double X, double Y) Normalize(double x, double y, double width, double height, IEnumerable<System.Windows.Forms.Screen> screens)
    {
        var area = screens.Select(s => s.WorkingArea).FirstOrDefault(a => x < a.Right && x + width > a.Left && y < a.Bottom && y + height > a.Top);
        if (area == default) area = screens.First().WorkingArea;
        return (Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }
}
