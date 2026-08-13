using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace QwenWorkOverlay;

// UI owns presentation only. Discovery, diagnostics, logging, screenshots and audio creation are
// explicitly kept out of this Dispatcher so a slow Qwen process cannot starve tray/hotkey input.
public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly AppLogger _log;
    private readonly ControllerRuntimeOptions _options;
    private readonly QwenProcessLocator _locator;
    private readonly WindowRecoveryService _recovery;
    private readonly QwenWindowController _qwen;
    private readonly QwenVoiceAutomation _voice;
    private readonly ForegroundWindowTracker _foregroundTracker;
    private readonly QwenSessionMonitor _sessionMonitor;
    private readonly DiagnosticsService _diagnostics = new();
    private readonly EmergencyRecoveryService _emergency;
    private readonly object _audioGate = new();
    private AudioDeviceService? _devices;
    private AudioDefaultDeviceGuard? _deviceGuard;
    private MixedAudioSession? _audio;
    private GlobalHotkeys? _hotkeys;
    private EmergencyHotkey? _emergencyHotkey;
    private Forms.NotifyIcon? _tray;
    private bool _resourcesDisposed;
    private bool _voiceToggleStartedByHotkey;
    private bool _trayStartupHandled;

    public MainWindow(SettingsService settings, AppLogger log, ControllerRuntimeOptions options)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _options = options;
        _locator = new QwenProcessLocator(log);
        _recovery = new WindowRecoveryService(log);
        _qwen = new QwenWindowController(log, _recovery);
        _voice = new QwenVoiceAutomation(log);
        _foregroundTracker = new ForegroundWindowTracker(() => _qwen.Target?.Hwnd);
        _sessionMonitor = new QwenSessionMonitor(() => _qwen.Target, _locator.FindRunningTarget, OnDiscoveryResult);
        _emergency = new EmergencyRecoveryService(_recovery, log, FreezeForEmergencyExit);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var pos = WindowStateNormalizer.Normalize(_settings.Current.ControllerX, _settings.Current.ControllerY, Width, Height, Forms.Screen.AllScreens);
        Left = pos.X;
        Top = pos.Y;
        CreateTrayIcon();
        _hotkeys = new GlobalHotkeys(HandleHotkey, _log);
        _emergencyHotkey = new EmergencyHotkey(_emergency.RequestExit, _log);
        _hotkeys.RightCtrlChanged += RightCtrlChanged;
        _hotkeys.VoiceToggleChanged += VoiceToggleChanged;
        if (!_hotkeys.AllRegistered) StatusText.Text = "Некоторые глобальные горячие клавиши недоступны: " + _hotkeys.FailureSummary;
        if (_options.SafeMode) StatusText.Text = "БЕЗОПАСНЫЙ РЕЖИМ: только наблюдательное подключение; управление окном, аудио и снимки отключены.";
        _sessionMonitor.Start();
        UpdateStatus();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть контроллёр", null, (_, _) => QueueUi(ShowController));
        menu.Items.Add("Показать Qwen", null, (_, _) => QueueUi(ShowQwen));
        menu.Items.Add("Диагностика", null, (_, _) => QueueUi(ShowDiagnostics));
        menu.Items.Add("Аварийное восстановление", null, (_, _) => _emergency.RequestExit());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти из контроллёра", null, (_, _) => _emergency.RequestExit());
        _tray = new Forms.NotifyIcon { Text = "Контроллёр Qwen Desktop", Icon = System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => QueueUi(ShowController);
    }

    private void OnDiscoveryResult(QwenTarget? target)
    {
        QueueUi(() =>
        {
            if (target is not null && (!_qwen.IsAttached || _qwen.Target?.Hwnd != target.Hwnd))
            {
                if (_qwen.Target is not null) _qwen.Detach(restore: false);
                if (_qwen.Attach(target))
                {
                    NativeStatusText.Text = "Подключено к установленному Qwen Desktop в режиме наблюдения";
                    NativeDetailsText.Text = $"{target.Summary}\n{target.ExecutablePath}\nКласс: {target.WindowClass}";
                    _log.Info("Native Qwen attached observationally");
                }
            }
            else if (target is null && !_qwen.IsAttached)
            {
                NativeStatusText.Text = "Установленный Qwen Desktop не подключён";
                NativeDetailsText.Text = "Откройте Qwen обычным способом или нажмите «Подключить / открыть Qwen». При подключении состояние окна не меняется.";
            }
            if (TrayStartupPolicy.ShouldHideAfterAttach(_settings.Current.StartControllerInTray, _qwen.IsAttached, _trayStartupHandled))
            {
                _trayStartupHandled = true;
                Hide();
            }
            UpdateStatus();
        });
    }

    private void QueueUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.BeginInvoke(action);
    }

    private void HandleHotkey(int id) => QueueUi(() => HandleHotkeyOnUi(id));

    private async void HandleHotkeyOnUi(int id)
    {
        if (!_qwen.IsAttached) _sessionMonitor.CheckNow();
        switch (id)
        {
            case 1: if (!_options.SafeMode && !_qwen.ToggleVisibility()) Toast("Qwen не подключён"); break;
            case 2: if (!RequireMutation()) break; ToggleClickThrough(); break;
            case 3: if (!RequireMutation()) break; ToggleTopMost(); break;
            case 5: if (RequireMutation()) SetOpacity(_settings.Current.Opacity + .05); break;
            case 6: if (RequireMutation()) SetOpacity(_settings.Current.Opacity - .05); break;
            case 7: await PasteClipboardIntoQwenAsync(); break;
            case 8: ShowDiagnostics(); break;
            case 9: await CaptureWorkWindowAsync(); break;
            case 10: await CaptureMonitorAsync(); break;
        }
        UpdateStatus();
    }

    private bool RequireMutation()
    {
        if (_options.SafeMode) { Toast("Безопасный режим блокирует изменение окна Qwen"); return false; }
        if (!_qwen.IsAttached) { Toast("Qwen не подключён"); return false; }
        return true;
    }

    private void VoiceToggleChanged(bool active) => QueueUi(() =>
    {
        if (_options.SafeMode) { Toast("Безопасный режим блокирует голосовой ввод"); return; }
        var hwnd = _qwen.Target?.Hwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) { _sessionMonitor.CheckNow(); Toast("Qwen не подключён"); return; }
        _ = Task.Run(() => _voice.TryInvokeVoiceButton(hwnd)).ContinueWith(t => QueueUi(() =>
        {
            _voiceToggleStartedByHotkey = active && t.Status == TaskStatus.RanToCompletion && t.Result;
            Toast(_voiceToggleStartedByHotkey ? "Голосовой ввод Qwen переключён" : UiText.VoiceState(_voice.State));
            UpdateStatus();
        }), TaskScheduler.Default);
    });

    private void RightCtrlChanged(bool down) => QueueUi(() =>
    {
        if (_options.SafeMode || !_settings.Current.RightCtrlAudioEnabled) return;
        _ = Task.Run(() =>
        {
            lock (_audioGate)
            {
                if (down)
                {
                    var audio = GetOrCreateAudio();
                    audio.Start(_settings.Current.MicrophoneDeviceId, _settings.Current.LoopbackDeviceId, _settings.Current.VirtualMixOutputDeviceId, _settings.Current.MicGain, _settings.Current.SystemGain);
                }
                else _audio?.Stop();
            }
        }).ContinueWith(_ => QueueUi(UpdateStatus), TaskScheduler.Default);
    });

    private MixedAudioSession GetOrCreateAudio()
    {
        if (_audio is not null) return _audio;
        _devices = new AudioDeviceService();
        _deviceGuard = new AudioDefaultDeviceGuard(_devices);
        return _audio = new MixedAudioSession(_devices, _log);
    }

    private void ToggleClickThrough()
    {
        var requested = !_qwen.ClickThrough;
        if (_qwen.SetClickThrough(requested)) { _settings.Current.ClickThrough = requested; _settings.Save(); Toast($"Сквозные клики: {UiText.Switch(requested)}"); }
        else Toast("Не удалось изменить режим сквозных кликов");
    }

    private void ToggleTopMost()
    {
        var requested = !_qwen.TopMost;
        if (_qwen.SetTopMost(requested)) { _settings.Current.TopMost = requested; _settings.Save(); Toast($"Поверх всех окон: {UiText.Switch(requested)}"); }
        else Toast("Не удалось изменить режим «поверх всех окон»");
    }

    private void SetOpacity(double value)
    {
        value = Math.Clamp(value, .35, 1);
        if (value < .999 && !_options.OpacityEnabled)
        {
            Toast("Прозрачность отключена до прохождения теста композитора на этом компьютере (--enable-opacity)");
            return;
        }
        if (_qwen.SetOpacity(value)) { _settings.Current.Opacity = value; _settings.Save(); Toast($"Прозрачность Qwen: {value:P0}"); }
        else Toast("Не удалось изменить прозрачность: композитор Qwen не поддерживает операцию");
    }

    private async Task PasteClipboardIntoQwenAsync()
    {
        if (_options.SafeMode) { Toast("Безопасный режим блокирует вставку"); return; }
        if (!_qwen.ShowAndActivate()) { Toast("Qwen не подключён"); return; }
        await Task.Delay(140);
        try { Forms.SendKeys.SendWait("^v"); Toast("Содержимое буфера обмена вставлено в Qwen"); } catch { Toast("Не удалось вставить из буфера обмена; используйте Ctrl+V вручную"); }
    }

    private async Task CaptureWorkWindowAsync()
    {
        if (_options.SafeMode) { Toast("Безопасный режим блокирует снимки экрана"); return; }
        var hwnd = _foregroundTracker.LastNonQwenWindow;
        var result = hwnd != IntPtr.Zero && await StaWork.RunAsync(() => _qwen.CaptureWorkWindowToClipboard(hwnd));
        Toast(result ? "Снимок скопирован в буфер обмена" : "Не удалось сделать снимок");
    }

    private async Task CaptureMonitorAsync()
    {
        if (_options.SafeMode) { Toast("Безопасный режим блокирует снимки экрана"); return; }
        var result = await StaWork.RunAsync(_qwen.CaptureMonitorToClipboard);
        Toast(result ? "Снимок монитора скопирован в буфер обмена" : "Не удалось сделать снимок");
    }

    private void UpdateStatus()
    {
        if (_qwen.IsAttached && _qwen.Target is { } target)
        {
            NativeStatusText.Text = "Подключено к установленному Qwen Desktop в режиме наблюдения";
            NativeDetailsText.Text = $"{target.Summary} · {target.WindowTitle}";
            WindowStatusText.Text = $"Прозрачность {_qwen.Opacity:P0} · Поверх всех окон {UiText.Switch(_qwen.TopMost)} · Сквозные клики {UiText.Switch(_qwen.ClickThrough)}";
        }
        else WindowStatusText.Text = "Управление окном: ожидание установленного Qwen Desktop";
        var audio = _audio;
        AudioStatusText.Text = audio is null ? "Аудиомикс: отключён до явного включения" : audio.Running ? "Аудиомикс: " + UiText.AudioState(audio.InjectionState) : "Аудиомикс: ожидание";
        PrivacyStatusText.Text = "Защита демонстрации: " + UiText.PrivacyStatus(_qwen.PrivacyStatus);
    }

    private void Toast(string text)
    {
        StatusText.Text = text;
        if (!IsVisible && _tray is not null) { _tray.BalloonTipTitle = "Контроллёр Qwen Desktop"; _tray.BalloonTipText = text; _tray.ShowBalloonTip(1200); }
    }

    private async void ShowDiagnostics()
    {
        var target = _qwen.Target;
        var window = new Window { Title = "Диагностика контроллёра Qwen Desktop", Width = 680, Height = 640, Content = new System.Windows.Controls.TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Text = BuildCachedDiagnostics(target) } };
        // A WPF owned window inherits the hidden state of its tray-host owner. Diagnostics must
        // remain reachable through Ctrl+Alt+D even after the controller panel has gone to tray.
        if (IsVisible) window.Owner = this;
        window.Show();
        window.Activate();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var metrics = await _diagnostics.CollectAsync(target, timeout.Token);
            if (window.Content is System.Windows.Controls.TextBox box && window.IsVisible) box.Text = BuildCachedDiagnostics(target) + "\n\n" + FormatProcess("Контроллёр", metrics.Controller) + "\n" + FormatProcess("Qwen", metrics.Qwen) + $"\nЗадержка Dispatcher: обновления UI поставлены в очередь (без синхронного Dispatcher.Invoke)\nПоследний вызов перехватчика клавиатуры: {_hotkeys?.LastHookDuration.TotalMilliseconds:F3} мс\nПринято сообщений журнала в минуту: {_log.AcceptedMessagesPerMinute:F1}\nОтброшено сообщений журнала: {_log.DroppedMessageCount}\nИсходный/текущий родитель: {FormatHwnd(_qwen.OriginalParent)}/{FormatHwnd(_qwen.CurrentParent)}\nЖурнал восстановления существует: {(File.Exists(_recovery.JournalPath) ? "Да" : "Нет")}";
        }
        catch (Exception ex) { _log.Error("Diagnostics collection failed: " + ex.GetType().Name); }
    }

    private string BuildCachedDiagnostics(QwenTarget? target) =>
        $"Версия контроллёра: {GetType().Assembly.GetName().Version}\nPID контроллёра: {Environment.ProcessId}\nБезопасный режим: {(_options.SafeMode ? "Да" : "Нет")}\nQwen подключён: {(_qwen.IsAttached ? "Да" : "Нет")}\nPID Qwen: {target?.ProcessId}\nHWND Qwen: {FormatHwnd(target?.Hwnd ?? IntPtr.Zero)}\nКласс Qwen: {target?.WindowClass}\nИсполняемый файл Qwen: {target?.ExecutablePath}\nСостояние подключения: наблюдательное\nПрозрачность: {_qwen.Opacity:P0}\nПоверх всех окон: {UiText.Switch(_qwen.TopMost)}\nСквозные клики: {UiText.Switch(_qwen.ClickThrough)}\nЗащита демонстрации: {UiText.PrivacyStatus(_qwen.PrivacyStatus)}\nЗапрошено/подтверждено WDA: 0x{_qwen.RequestedAffinity:X}/0x{_qwen.VerifiedAffinity:X}\nГорячие клавиши: {_hotkeys?.FailureSummary ?? "Инициализация"}\nПерехватчик Right Ctrl: {(_hotkeys?.HookReady == true ? "Готово" : "Ошибка")}\nАварийная клавиша Ctrl+Alt+Esc: {UiText.EmergencyHotkeyStatus(_emergencyHotkey?.Status)}\nАудио: {(_audio?.Running == true ? "работает" : "отключено / ожидание")}\nПоследний/максимальный цикл аудио: {_audio?.LastPumpDuration.TotalMilliseconds:F3}/{_audio?.MaxPumpDuration.TotalMilliseconds:F3} мс\nЖурнал восстановления: {_recovery.JournalPath}\nЖурнал: {_log.LogPath}\n\nСбор метрик процессов асинхронно…";

    private static string FormatProcess(string label, ProcessDiagnostics value) => $"{label}: PID={value.Pid}, CPU={value.CpuPercent:F2}%, Рабочий набор={value.WorkingSetBytes / 1024d / 1024d:F1} MiB, Потоки={value.ThreadCount}, Дескрипторы={value.HandleCount}, GDI={value.GdiObjectCount}, USER={value.UserObjectCount}, Состояние={UiText.ProcessState(value.State)}";
    private static string FormatHwnd(IntPtr hwnd) => hwnd == IntPtr.Zero ? "нет" : $"0x{hwnd.ToInt64():X}";

    private void ShowController() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ShowQwen() { if (!_qwen.ShowAndActivate()) { _sessionMonitor.CheckNow(); Toast("Qwen не подключён"); } }
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(() =>
        {
            var path = _locator.FindInstalledExecutable(_settings.Current.QwenExecutablePath);
            if (!string.IsNullOrWhiteSpace(path) && _settings.Current.AutoLaunchQwen) _locator.TryLaunch(path);
            _sessionMonitor.CheckNow();
        });
        Toast("Поиск установленного Qwen Desktop…");
    }
    private void ShowQwen_Click(object sender, RoutedEventArgs e) => ShowQwen();
    private void Click_Click(object sender, RoutedEventArgs e) { if (RequireMutation()) ToggleClickThrough(); }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Endpoint enumeration is user-initiated and the service is retained for orderly shutdown.
        _devices ??= new AudioDeviceService();
        _deviceGuard ??= new AudioDefaultDeviceGuard(_devices);
        new SettingsWindow(_settings, _devices).ShowDialog();
        UpdateStatus();
    }
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => ShowDiagnostics();
    private void Exit_Click(object sender, RoutedEventArgs e) => _emergency.RequestExit();

    public void EmergencyRestoreForCrash() => _emergency.RequestExit();

    private void FreezeForEmergencyExit()
    {
        _qwen.FreezeMutations();
        if (!Monitor.TryEnter(_audioGate))
        {
            _log.Error("Emergency recovery skipped audio stop because the audio gate was busy");
            return;
        }
        try { _audio?.Stop(); } catch { }
        finally { Monitor.Exit(_audioGate); }
    }

    private void DisposeRuntimeResources(bool saveControllerPosition)
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        _sessionMonitor.Dispose();
        try { _audio?.Stop(); } catch { }
        try { _qwen.Dispose(); } catch { }
        try { _foregroundTracker.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _emergencyHotkey?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _devices?.Dispose(); } catch { }
        if (saveControllerPosition) { _settings.Current.ControllerX = Left; _settings.Current.ControllerY = Top; _settings.Save(); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
