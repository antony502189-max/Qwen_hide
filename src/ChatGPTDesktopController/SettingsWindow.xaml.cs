namespace ChatGPTDesktopController;

public partial class SettingsWindow : Window
{
    private readonly ControllerSettings _settings;
    public SettingsWindow(ControllerSettings settings, AudioDeviceService devices)
    {
        InitializeComponent(); _settings = settings;
        StartInTray.IsChecked = settings.StartInTray; AutoLaunch.IsChecked = settings.AutoLaunchTarget; ExecutablePath.Text = settings.ExecutablePath ?? ""; VoiceHotkey.Text = settings.VoiceHotkey; RightCtrlAudio.IsChecked = settings.RightCtrlAudioEnabled;
        Microphone.ItemsSource = devices.Inputs(); Loopback.ItemsSource = devices.Outputs(); VirtualOutput.ItemsSource = devices.Outputs(); Microphone.SelectedValue = settings.PhysicalMicrophoneId; Loopback.SelectedValue = settings.LoopbackDeviceId; VirtualOutput.SelectedValue = settings.VirtualOutputId;
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        _settings.StartInTray = StartInTray.IsChecked == true; _settings.AutoLaunchTarget = AutoLaunch.IsChecked == true;
        _settings.ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath.Text) ? null : ExecutablePath.Text.Trim();
        _settings.RightCtrlAudioEnabled = RightCtrlAudio.IsChecked == true; _settings.PhysicalMicrophoneId = Microphone.SelectedValue as string; _settings.LoopbackDeviceId = Loopback.SelectedValue as string; _settings.VirtualOutputId = VirtualOutput.SelectedValue as string;
        SettingsService.Save(_settings); DialogResult = true;
    }
}
