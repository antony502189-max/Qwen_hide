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
    public string? VirtualMixOutputDeviceId { get; set; }
    public float MicGain { get; set; } = 1f;
    public float SystemGain { get; set; } = 1f;
    public bool RightCtrlAudioEnabled { get; set; } = true;
    public bool AutoToggleQwenVoiceWithRightCtrl { get; set; } = true;

    public double ControllerX { get; set; } = 120;
    public double ControllerY { get; set; } = 120;
}

public sealed class SettingsService
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenDesktopController");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public AppSettings Current { get; private set; } = new();
    public string? LastPersistenceError { get; private set; }

    public void Load()
    {
        Directory.CreateDirectory(Root);
        LastPersistenceError = null;
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Current = new AppSettings();
            LastPersistenceError = "Settings load failed: " + ex.GetType().Name;
        }

        Normalize();
    }

    public bool Save()
    {
        LastPersistenceError = null;
        try
        {
            Normalize();
            Directory.CreateDirectory(Root);
            var temp = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, true);
            return true;
        }
        catch (Exception ex)
        {
            LastPersistenceError = "Settings save failed: " + ex.GetType().Name;
            return false;
        }
    }

    private void Normalize()
    {
        Current.Opacity = Math.Clamp(Current.Opacity, .35, 1.0);
        Current.MicGain = Math.Clamp(Current.MicGain, 0f, 4f);
        Current.SystemGain = Math.Clamp(Current.SystemGain, 0f, 4f);
        Current.ControllerX = double.IsFinite(Current.ControllerX) ? Current.ControllerX : 120;
        Current.ControllerY = double.IsFinite(Current.ControllerY) ? Current.ControllerY : 120;
        if (!string.IsNullOrWhiteSpace(Current.QwenExecutablePath))
            Current.QwenExecutablePath = Environment.ExpandEnvironmentVariables(Current.QwenExecutablePath.Trim().Trim('"'));
    }
}

public static class WindowStateNormalizer
{
    public static (double X, double Y) Normalize(double x, double y, double width, double height, IEnumerable<System.Windows.Forms.Screen> screens)
    {
        var array = screens.ToArray();
        if (array.Length == 0) return (x, y);
        if (!double.IsFinite(x)) x = array[0].WorkingArea.Left;
        if (!double.IsFinite(y)) y = array[0].WorkingArea.Top;
        if (!double.IsFinite(width) || width <= 0) width = 470;
        if (!double.IsFinite(height) || height <= 0) height = 330;

        var area = array.Select(s => s.WorkingArea)
            .FirstOrDefault(a => x < a.Right && x + width > a.Left && y < a.Bottom && y + height > a.Top);
        if (area == default) area = array[0].WorkingArea;
        return (
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }
}
