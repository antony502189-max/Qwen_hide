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
        Title = "Настройки контроллера Qwen Desktop";
        Width = 690;
        Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
        Foreground = System.Windows.Media.Brushes.White;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = panel;
        Content = scroll;
        var s = service.Current;

        panel.Children.Add(Heading("Установленный Qwen Desktop"));
        var qwenPath = new TextBox { Text = s.QwenExecutablePath ?? string.Empty, MinWidth = 420 };
        var pathRow = new DockPanel();
        var browse = new Button { Content = "Обзор…", Width = 88, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Qwen|Qwen.exe|Исполняемые файлы|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) qwenPath.Text = dialog.FileName;
        };
        pathRow.Children.Add(browse);
        pathRow.Children.Add(qwenPath);
        Add(panel, "Путь к Qwen.exe (необязательно; по возможности определяется автоматически)", pathRow);

        var autoLaunch = new CheckBox { Content = "Автоматически запускать установленный Qwen, если он не запущен", IsChecked = s.AutoLaunchQwen, Margin = new Thickness(0, 6, 0, 0) };
        var tray = new CheckBox { Content = "После подключения Qwen скрывать окно контроллера в системный трей", IsChecked = s.StartControllerInTray, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(autoLaunch);
        panel.Children.Add(tray);

        panel.Children.Add(Heading("Управление окном"));
        var opacity = new Slider { Minimum = .35, Maximum = 1, Value = s.Opacity, TickFrequency = .05, IsSnapToTickEnabled = true };
        Add(panel, "Прозрачность Qwen (35%–100%)", opacity);
        var top = new CheckBox { Content = "Держать Qwen поверх всех окон", IsChecked = s.TopMost };
        panel.Children.Add(top);

        panel.Children.Add(Heading("Отдельный аудиомикс для Qwen"));
        panel.Children.Add(new TextBlock
        {
            Text = "Контроллер захватывает физический микрофон и звук Windows в совместном режиме, смешивает их и может отправлять микс только в распознанный виртуальный аудиокабель. Микрофон Windows по умолчанию контроллер не меняет.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 195)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var mic = new ComboBox { ItemsSource = devices.Inputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.MicrophoneDeviceId };
        var loopback = new ComboBox { ItemsSource = devices.Outputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.LoopbackDeviceId };
        var virtualMix = new ComboBox { ItemsSource = devices.Outputs(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = s.VirtualMixOutputDeviceId };
        Add(panel, "Физический микрофон", mic);
        Add(panel, "Устройство воспроизведения Windows для захвата звука собеседника", loopback);
        Add(panel, "Виртуальный аудиокабель для микса Qwen", virtualMix);

        var gains = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var mg = new TextBox { Text = s.MicGain.ToString("0.00", CultureInfo.InvariantCulture), Width = 72 };
        var sg = new TextBox { Text = s.SystemGain.ToString("0.00", CultureInfo.InvariantCulture), Width = 72 };
        gains.Children.Add(new TextBlock { Text = "Микрофон", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        gains.Children.Add(mg);
        gains.Children.Add(new TextBlock { Text = "Системный звук", Width = 130, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        gains.Children.Add(sg);
        panel.Children.Add(gains);

        var right = new CheckBox { Content = "Удерживать Right Ctrl, чтобы подавать настроенный микс в виртуальный аудиокабель", IsChecked = s.RightCtrlAudioEnabled, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(right);

        var appAudioSettings = new Button { Content = "Открыть настройки звука Windows для приложений", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        appAudioSettings.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
            catch { MessageBox.Show(this, "Не удалось открыть настройки звука Windows.", "Контроллер Qwen Desktop"); }
        };
        panel.Children.Add(appAudioSettings);

        panel.Children.Add(new TextBlock
        {
            Text = "Если Qwen позволяет выбрать устройство ввода, укажите микрофонную сторону, связанную с виртуальным аудиокабелем. Если выбора нет, при поддержке Windows можно вручную использовать маршрутизацию ввода для конкретного приложения. Не меняйте глобальный микрофон Windows только ради контроллера. Автоматизация голосовой кнопки работает по возможности; если Qwen не предоставляет кнопку через средства доступности Windows, используйте голосовой ввод вручную.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 195)),
            Margin = new Thickness(0, 8, 0, 0)
        });

        var save = new Button { Content = "Сохранить", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        save.Click += (_, _) =>
        {
            var requestedQwenPath = string.IsNullOrWhiteSpace(qwenPath.Text) ? null : qwenPath.Text.Trim().Trim('"');
            if (requestedQwenPath is not null)
            {
                requestedQwenPath = Environment.ExpandEnvironmentVariables(requestedQwenPath);
                if (!File.Exists(requestedQwenPath) || !Path.GetFileName(requestedQwenPath).Equals("Qwen.exe", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this,
                        "Выбранный путь должен указывать на существующий Qwen.exe. Оставьте поле пустым для автоматического поиска.",
                        "Неверный путь к Qwen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var selectedLoopback = loopback.SelectedValue as string;
            var selectedVirtualMix = virtualMix.SelectedValue as string;
            if (!string.IsNullOrWhiteSpace(selectedVirtualMix) &&
                !devices.ValidateVirtualMixOutput(selectedVirtualMix, selectedLoopback, out _))
            {
                MessageBox.Show(this,
                    "Выбранное устройство для аудиомикса отклонено из соображений безопасности.\n\nВыберите отдельный виртуальный аудиокабель, а не физические динамики, наушники или устройство Windows по умолчанию.",
                    "Небезопасное устройство аудиомикса",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            s.QwenExecutablePath = requestedQwenPath;
            s.AutoLaunchQwen = autoLaunch.IsChecked == true;
            s.StartControllerInTray = tray.IsChecked == true;
            s.Opacity = opacity.Value;
            s.TopMost = top.IsChecked == true;
            s.MicrophoneDeviceId = mic.SelectedValue as string;
            s.LoopbackDeviceId = selectedLoopback;
            s.VirtualMixOutputDeviceId = selectedVirtualMix;
            s.RightCtrlAudioEnabled = right.IsChecked == true;

            if (!float.TryParse(mg.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var micGain)) micGain = 1f;
            if (!float.TryParse(sg.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var systemGain)) systemGain = 1f;
            s.MicGain = Math.Clamp(micGain, 0f, 4f);
            s.SystemGain = Math.Clamp(systemGain, 0f, 4f);

            if (!service.Save())
            {
                MessageBox.Show(this,
                    "Не удалось сохранить настройки контроллера.",
                    "Ошибка сохранения настроек",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
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
