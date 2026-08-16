namespace ChatGPTDesktopController;

public partial class SettingsWindow : Window
{
    private readonly ControllerSettings _settings;
    public SettingsWindow(ControllerSettings settings)
    {
        InitializeComponent(); _settings = settings;
        StartInTray.IsChecked = settings.StartInTray; AutoLaunch.IsChecked = settings.AutoLaunchTarget; ExecutablePath.Text = settings.ExecutablePath ?? ""; VoiceHotkey.Text = settings.VoiceHotkey;
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        _settings.StartInTray = StartInTray.IsChecked == true; _settings.AutoLaunchTarget = AutoLaunch.IsChecked == true;
        _settings.ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath.Text) ? null : ExecutablePath.Text.Trim();
        SettingsService.Save(_settings); DialogResult = true;
    }
}
