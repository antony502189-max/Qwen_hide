using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace QwenWorkOverlay;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(SettingsService service, AudioDeviceService devices)
    {
        Title = "Qwen Desktop Controller Settings";
        Width = 620;
        Height = 690;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
        Foreground = System.Windows.Media.Brushes.White;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = panel;
        Content = scroll;
        var s = service.Current;

        panel.Children.Add(Heading("Installed Qwen Desktop"));
        var qwenPath = new TextBox { Text = s.QwenExecutablePath ?? string.Empty, MinWidth = 420 };
        var pathRow = new DockPanel();
        var browse = new Button { Content = "Browse…", Width = 88, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Qwen executable|Qwen.exe|Executable files|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) qwenPath.Text = dialog.FileName;
        };
        pathRow.Children.Add(browse);
        pathRow.Children.Add(qwenPath);
        Add(panel, "Qwen.exe path (optional; auto-detected when possible)", pathRow);

        var autoLaunch = new CheckBox { Content = "Launch installed Qwen automatically when it is not running", IsChecked = s.AutoLaunchQwen, Margin = new Thickness(0, 6, 0, 0) };
        var tray = new CheckBox { Content = "Start controller panel hidden in the system tray after Qwen is attached", IsChecked = s.StartControllerInTray, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(autoLaunch);
        panel.Children.Add(tray);

        panel.Children.Add(Heading("Window controls"));
        var opacity = new Slider { Minimum = .35, Maximum = 1, Value = s.Opacity, TickFrequency = .05, IsSnapToTickEnabled = true };
        Add(panel, "Qwen opacity (35%–100%)", opacity);
        var top = new CheckBox { Content = "Keep Qwen always on top", IsChecked = s.TopMost };
        panel.Children.Add(top);

        panel.Children.Add(Heading("Qwen-only audio mix"));
        panel.Children.Add(new TextBlock
        {
            Text = "The controller captures the physical microphone and Windows playback in shared mode, mixes them, and can render the mix only to a recognized virtual cable. It never changes the Windows default microphone.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 195)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var mic = new ComboBox { ItemsSource = devices.Inputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.MicrophoneDeviceId };
        var loopback = new ComboBox { ItemsSource = devices.Outputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.LoopbackDeviceId };
        var virtualMix = new ComboBox { ItemsSource = devices.Outputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.VirtualMixOutputDeviceId };
        Add(panel, "Physical microphone", mic);
        Add(panel, "Windows playback endpoint to hear the other participant", loopback);
        Add(panel, "Virtual cable render endpoint used only for the Qwen mix", virtualMix);

        var gains = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var mg = new TextBox { Text = s.MicGain.ToString("0.00", CultureInfo.InvariantCulture), Width = 72 };
        var sg = new TextBox { Text = s.SystemGain.ToString("0.00", CultureInfo.InvariantCulture), Width = 72 };
        gains.Children.Add(new TextBlock { Text = "Mic gain", Width = 80, VerticalAlignment = VerticalAlignment.Center });
        gains.Children.Add(mg);
        gains.Children.Add(new TextBlock { Text = "System gain", Width = 100, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        gains.Children.Add(sg);
        panel.Children.Add(gains);

        var right = new CheckBox { Content = "Hold Right Ctrl to feed the configured mix to the virtual cable", IsChecked = s.RightCtrlAudioEnabled, Margin = new Thickness(0, 8, 0, 0) };
        var voiceToggle = new CheckBox { Content = "Also try to toggle Qwen's existing voice button on Right Ctrl down/up", IsChecked = s.AutoToggleQwenVoiceWithRightCtrl, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(right);
        panel.Children.Add(voiceToggle);

        var appAudioSettings = new Button { Content = "Open Windows per-app audio settings", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        appAudioSettings.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
            catch { MessageBox.Show(this, "Could not open Windows sound settings.", "Qwen Desktop Controller"); }
        };
        panel.Children.Add(appAudioSettings);

        panel.Children.Add(new TextBlock
        {
            Text = "If Qwen lets you choose an input device, select the capture side paired with the virtual cable. If it does not, Windows per-app input routing may be used manually where supported. Do not change the global default microphone just for this controller. Voice-button automation is best-effort and falls back to manual use if Qwen does not expose the button through Windows accessibility.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 195)),
            Margin = new Thickness(0, 8, 0, 0)
        });

        var save = new Button { Content = "Save", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        save.Click += (_, _) =>
        {
            s.QwenExecutablePath = string.IsNullOrWhiteSpace(qwenPath.Text) ? null : qwenPath.Text.Trim();
            s.AutoLaunchQwen = autoLaunch.IsChecked == true;
            s.StartControllerInTray = tray.IsChecked == true;
            s.Opacity = opacity.Value;
            s.TopMost = top.IsChecked == true;
            s.MicrophoneDeviceId = mic.SelectedValue as string;
            s.LoopbackDeviceId = loopback.SelectedValue as string;
            s.VirtualMixOutputDeviceId = virtualMix.SelectedValue as string;
            s.RightCtrlAudioEnabled = right.IsChecked == true;
            s.AutoToggleQwenVoiceWithRightCtrl = voiceToggle.IsChecked == true;

            if (!float.TryParse(mg.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var micGain)) micGain = 1f;
            if (!float.TryParse(sg.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var systemGain)) systemGain = 1f;
            s.MicGain = Math.Clamp(micGain, 0f, 4f);
            s.SystemGain = Math.Clamp(systemGain, 0f, 4f);
            service.Save();
            Close();
        };
        panel.Children.Add(save);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 15,
        Margin = new Thickness(0, 14, 0, 6)
    };

    private static void Add(System.Windows.Controls.Panel panel, string label, FrameworkElement control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        panel.Children.Add(control);
    }
}
