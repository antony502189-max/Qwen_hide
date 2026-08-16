using System.Diagnostics;
using System.Reflection;

namespace ChatGPTDesktopController;

public partial class MainWindow : Window
{
    private readonly AppLogger _log; private readonly ChatGPTProcessLocator _locator; private readonly RecoveryService _recovery; private readonly WindowController _window; private readonly ComposerAutomation _paste; private readonly VoiceAutomation _voice; private readonly GlobalHotkeys _hotkeys; private readonly EmergencyHotkey _emergency; private readonly TrayController _tray; private readonly ControllerSettings _settings; private readonly System.Windows.Threading.DispatcherTimer _reacquireTimer; private readonly object _audioGate = new(); private AudioDeviceService? _audioDevices; private MixedAudioSession? _audio;
    private ScreenshotResult _capture = new(false, "Not run", DateTimeOffset.MinValue);
    public MainWindow(AppLogger log)
    {
        InitializeComponent(); _log = log; _settings = SettingsService.Load(); _locator = new(log); _recovery = new(log); _window = new(log, _recovery); _paste = new(log); _voice = new(log);
        _hotkeys = new GlobalHotkeys(HandleHotkey); _hotkeys.RightCtrlChanged += RightCtrlChanged; _emergency = new EmergencyHotkey(Emergency); _tray = new TrayController(ShowController, RefreshAndShowDiagnostics, ExitSafely);
        Attach(); TryAutoLaunch(); RefreshDiagnostics();
        _reacquireTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; _reacquireTimer.Tick += (_, _) => { if (!_window.IsAttached) { Attach(); RefreshDiagnostics(); } }; _reacquireTimer.Start();
        Loaded += (_, _) => { if (_settings.StartInTray) Hide(); };
    }
    private void HandleHotkey(int id) => Dispatcher.BeginInvoke(async () =>
    {
        switch (id)
        {
            case 1: EnsureAttached(); _window.ToggleVisibility(); break; case 2: EnsureAttached(); _window.ToggleClickThrough(); break; case 3: EnsureAttached(); _window.ToggleTopMost(); break;
            case 4: EnsureAttached(); _window.AdjustOpacity(.05); break; case 5: EnsureAttached(); _window.AdjustOpacity(-.05); break;
            case 6: EnsureAttached(); await _paste.PasteImageAsync(_window.Target, _window); break;
            case 7: EnsureAttached(); RefreshDiagnostics(); Activate(); break;
            case 8: _capture = ScreenshotService.CaptureActiveWindowToClipboard(_window.Target?.Hwnd ?? IntPtr.Zero); _log.Info("F6: " + _capture.Detail); break;
            case 9: EnsureAttached(); _voice.Probe(_window.Target); _window.EnsureInteractive(() => _voice.Invoke(_window.Target)); break;
        }
        RefreshDiagnostics();
    });
    private void Attach() { var target = _locator.FindRunningTarget(); if (target is not null) _window.Attach(target); else _log.Info("ChatGPT Classic target not running."); }
    private void TryAutoLaunch() { if (_window.IsAttached || !_settings.AutoLaunchTarget) return; var executable = _locator.FindInstalledExecutable(_settings.ExecutablePath); if (executable is not null) _locator.TryLaunch(executable); }
    private void EnsureAttached() { if (!_window.IsAttached) Attach(); }
    private void RightCtrlChanged(bool down)
    {
        if (!_settings.RightCtrlAudioEnabled) return;
        Task.Run(() => { lock (_audioGate) { if (down) GetOrCreateAudio().Start(_settings); else _audio?.Stop(); } }).ContinueWith(_ => Dispatcher.BeginInvoke(RefreshDiagnostics));
    }
    private MixedAudioSession GetOrCreateAudio() => _audio ??= new MixedAudioSession(_audioDevices ??= new AudioDeviceService(), _log);
    private void Emergency() { _window.Restore(); Dispatcher.BeginInvoke(Close); }
    private void RefreshDiagnostics()
    {
        var process = Process.GetCurrentProcess(); var target = _window.Target; var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        StatusText.Text = target is null ? "ChatGPT Classic not attached. Open the installed ChatGPT Classic app, then click Attach / refresh." : "Attached: " + target.Summary;
        DetailsText.Text = $"Controller\n  version: {version}\n  PID: {process.Id}\n  RAM: {process.WorkingSet64 / 1024 / 1024} MB\n  threads: {process.Threads.Count}\n  handles: {process.HandleCount}\n\nChatGPT Classic\n  attached: {_window.IsAttached}\n  PID: {target?.ProcessId}\n  HWND: 0x{target?.Hwnd.ToInt64():X}\n  executable: {target?.ExecutablePath}\n  process: {target?.ProcessName}\n  class: {target?.WindowClass}\n  title: {target?.WindowTitle}\n  architecture: x64 controller; Electron/Chromium package observed\n\nHotkeys\n  {string.Join("\n  ", _hotkeys.Registrations.Select(x => $"{x.Name}: {(x.Registered ? "registered" : "FAILED " + x.Win32Error)}"))}\n  Ctrl+Alt+Esc emergency: {(_emergency.Registered ? "registered" : "FAILED " + _emergency.Win32Error)}\n\nWindow\n  opacity: {_window.Opacity:P0}\n  TopMost: {_window.TopMost}\n  click-through: {_window.ClickThrough}\n  hidden: {_window.Hidden}\n\nScreenshot\n  last: {_capture.Detail}\n\nPaste\n  stage: {_paste.LastResult.Stage}\n  method: {_paste.LastResult.Method}\n  detail: {_paste.LastResult.Detail}\n\nVoice\n  native shortcut: {_voice.Status.Shortcut}\n  last: {_voice.Status.LastInvocation}\n  fallback: {_voice.Status.FallbackState}\n\nAudio\n  Right Ctrl audio: disabled until a safe dedicated virtual endpoint is configured; defaults are never changed.\n\nRecovery\n  journal exists: {_recovery.HasPendingSnapshot}\n  path: {_recovery.JournalPath}";
    }
    private void AttachClick(object sender, RoutedEventArgs e) { Attach(); RefreshDiagnostics(); }
    private void DiagnosticsClick(object sender, RoutedEventArgs e) { _voice.Probe(_window.Target); RefreshDiagnostics(); }
    private void SettingsClick(object sender, RoutedEventArgs e) { var dialog = new SettingsWindow(_settings, _audioDevices ??= new AudioDeviceService()) { Owner = this }; if (dialog.ShowDialog() == true) { TryAutoLaunch(); RefreshDiagnostics(); } }
    private void RestoreClick(object sender, RoutedEventArgs e) { _window.Restore(); RefreshDiagnostics(); }
    private void ExitClick(object sender, RoutedEventArgs e) => ExitSafely();
    private void ShowController() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void RefreshAndShowDiagnostics() { _voice.Probe(_window.Target); RefreshDiagnostics(); ShowController(); }
    private void ExitSafely() => Close();
    protected override void OnClosed(EventArgs e) { _reacquireTimer.Stop(); lock (_audioGate) { _audio?.Dispose(); _audioDevices?.Dispose(); } _tray.Dispose(); _hotkeys.Dispose(); _emergency.Dispose(); _window.Dispose(); base.OnClosed(e); }
}
