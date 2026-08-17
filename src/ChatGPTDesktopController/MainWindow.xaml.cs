using System.Diagnostics;
using System.Reflection;

namespace ChatGPTDesktopController;

public partial class MainWindow : Window
{
    private readonly AppLogger _log;
    private readonly ChatGPTProcessLocator _locator;
    private readonly RecoveryService _recovery;
    private readonly WindowController _window;
    private readonly ComposerAutomation _paste;
    private readonly VoiceAutomation _voice;
    private readonly GlobalHotkeys _hotkeys;
    private readonly EmergencyHotkey _emergency;
    private readonly TrayController _tray;
    private readonly PrivacyGuardService _privacy;
    private readonly PrivacyTransitionCoordinator _privacyTransitions;
    private readonly ControllerSettings _settings;
    private readonly System.Windows.Threading.DispatcherTimer _reacquireTimer;
    private readonly object _audioGate = new();
    private AudioDeviceService? _audioDevices;
    private MixedAudioSession? _audio;
    private ScreenshotResult _capture = new(false, "Not run", DateTimeOffset.MinValue);
    private int _shutdownStarted;

    public MainWindow(AppLogger log)
    {
        InitializeComponent(); _log = log; _settings = SettingsService.Load(); _locator = new(log); _recovery = new(log);
        _window = new(log, _recovery); _paste = new(log); _voice = new(log); _hotkeys = new GlobalHotkeys(HandleHotkey);
        _hotkeys.RightCtrlChanged += RightCtrlChanged; _emergency = new EmergencyHotkey(Emergency); _tray = new TrayController(ShowController, RefreshAndShowDiagnostics, ExitSafely);

        // Capture protection is intentionally isolated from WindowController and all stable hotkeys.
        // It only applies/verifies Windows display affinity and never rewrites opacity/TopMost/click-through state.
        _privacy = new PrivacyGuardService(log);
        _privacyTransitions = new PrivacyTransitionCoordinator(_privacy, log);
        SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _privacy.ProtectControllerOwnedWindow(hwnd);
            }
            catch (Exception ex) { _log.Error("Controller privacy initialization failed: " + ex.GetType().Name); }
        };

        Attach(); TryAutoLaunch(); RefreshDiagnostics();
        _reacquireTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _reacquireTimer.Tick += (_, _) => ReacquireIfNeeded();
        _reacquireTimer.Start();
    }

    private void HandleHotkey(int id) => Dispatcher.BeginInvoke(async () =>
    {
        if (Volatile.Read(ref _shutdownStarted) != 0) return;
        try
        {
            switch (id)
            {
                case 1:
                {
                    EnsureAttached();
                    var wasHidden = _window.Hidden;
                    var toggled = _window.ToggleVisibility();
                    if (toggled && wasHidden && !_window.Hidden && _window.Target is { } shownTarget)
                    {
                        // Electron can clear WDA as the real window is shown. Immediately request repair,
                        // then fail closed if the real HWND cannot be externally verified at 0x11.
                        _privacyTransitions.TrackPrimaryTarget(shownTarget);
                        _privacyTransitions.NotifyVisibilityOrLifecycleTransition();
                        var verified = await _privacyTransitions.EnsureVerifiedAfterShowAsync(
                            shownTarget.Hwnd, TimeSpan.FromMilliseconds(900));
                        if (!verified && _window.IsAttached && !_window.Hidden)
                        {
                            _log.Error("Privacy fail-closed: ChatGPT show was not verified capture-excluded; hiding target again.");
                            _window.ToggleVisibility();
                        }
                    }
                    break;
                }
                case 2: EnsureAttached(); _window.ToggleClickThrough(); break;
                case 3: EnsureAttached(); _window.ToggleTopMost(); break;
                case 4: EnsureAttached(); _window.AdjustOpacity(.05); break;
                case 5: EnsureAttached(); _window.AdjustOpacity(-.05); break;
                case 6: EnsureAttached(); await _paste.PasteImageAsync(_window.Target, _window); break;
                case 7: EnsureAttached(); _voice.Probe(_window.Target); ShowController(); break;
                case 8: _capture = ScreenshotService.CaptureActiveMonitorToClipboard(_window.Target?.Hwnd ?? IntPtr.Zero); _log.Info("F6: " + _capture.Detail); break;
                case 9: EnsureAttached(); _voice.Probe(_window.Target); _window.EnsureInteractive(() => _voice.Invoke(_window.Target)); break;
            }
        }
        catch (Exception ex) { _log.Error($"Hotkey {id} failed: {ex.GetType().Name}"); }
        finally { if (Volatile.Read(ref _shutdownStarted) == 0) RefreshDiagnostics(); }
    });

    private void Attach()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0) return;
        var target = _locator.FindRunningTarget();
        if (target is not null)
        {
            _window.Attach(target);
            _privacyTransitions.TrackPrimaryTarget(target);
            _privacy.ScanNow();
        }
        else
        {
            _privacyTransitions.TrackPrimaryTarget(null);
            _log.Info("ChatGPT Classic target not running.");
        }
    }

    private void ReacquireIfNeeded()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0) return;
        _tray.UpdatePrivacy(_privacy.Snapshot);
        if (_window.IsAttached)
        {
            if (IsVisible) RefreshDiagnostics();
            return;
        }
        Attach(); TryAutoLaunch(); RefreshDiagnostics();
    }
    private void TryAutoLaunch() { if (Volatile.Read(ref _shutdownStarted) != 0 || _window.IsAttached || !_settings.AutoLaunchTarget) return; var path = _locator.FindInstalledExecutable(_settings.ExecutablePath); if (path is not null) _locator.TryLaunch(path); }
    private void EnsureAttached() { if (!_window.IsAttached) Attach(); }
    private void RightCtrlChanged(bool down)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0 || !_settings.RightCtrlAudioEnabled) return;
        Task.Run(() => { lock (_audioGate) { if (down) GetOrCreateAudio().Start(_settings); else _audio?.Stop(); } }).ContinueWith(_ => { if (Volatile.Read(ref _shutdownStarted) == 0) Dispatcher.BeginInvoke(RefreshDiagnostics); });
    }
    private MixedAudioSession GetOrCreateAudio() => _audio ??= new MixedAudioSession(_audioDevices ??= new AudioDeviceService(), _log);
    private void Emergency()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        _window.Restore();
        lock (_audioGate) _audio?.Stop();
        Dispatcher.BeginInvoke(Close);
    }

    private void RefreshDiagnostics()
    {
        using var process = Process.GetCurrentProcess(); var target = _window.Target; var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var privacy = _privacy.Snapshot;
        var transitions = _privacyTransitions.Snapshot;
        _tray.UpdatePrivacy(privacy);
        StatusText.Text = target is null ? "ChatGPT Classic not attached. Open the installed ChatGPT Classic app, then click Attach / refresh." : "Attached: " + target.Summary;
        var lines = new List<string>
        {
            "Controller", $"  version: {version}", $"  PID: {process.Id}", $"  RAM: {process.WorkingSet64 / 1024 / 1024} MB", $"  threads: {process.Threads.Count}", $"  handles: {process.HandleCount}", "",
            "ChatGPT Classic", $"  attached: {_window.IsAttached}", $"  PID: {target?.ProcessId}", $"  HWND: 0x{target?.Hwnd.ToInt64():X}", $"  executable: {target?.ExecutablePath}", $"  process: {target?.ProcessName}", $"  class: {target?.WindowClass}", $"  title: {target?.WindowTitle}", $"  architecture: {target?.Architecture ?? "unknown"}", "",
            "Capture privacy", $"  state: {privacy.State}", $"  DWM composing: {privacy.DwmComposing}", $"  protected windows: {privacy.WindowsProtected}/{privacy.WindowsSeen}", $"  externally verified: {privacy.WindowsVerified}/{privacy.WindowsSeen}", $"  failed windows: {privacy.WindowsFailed}", $"  detail: {privacy.Detail}", $"  primary verified: {transitions.PrimaryVerified}", $"  primary affinity: {transitions.Affinity}", $"  watchdog repairs requested: {transitions.RepairRequests}", $"  transition detail: {transitions.Detail}", "  mode: WDA_EXCLUDEFROMCAPTURE, event-driven repair + primary watchdog + transition burst verification", "  fail-closed: a user-triggered show is reverted to hidden if 0x11 cannot be externally verified", "  boundary: best-effort Windows public-capture protection; not a universal DRM guarantee", "",
            "Hotkeys"
        };
        lines.AddRange(_hotkeys.Registrations.Select(x => $"  {x.Name}: {(x.Registered ? "registered" : "FAILED " + x.Win32Error)}"));
        lines.AddRange([$"  Right Ctrl audio hook: {(_hotkeys.RightCtrlHookReady ? "registered" : "FAILED")}", $"  Ctrl+Alt+Esc emergency: {(_emergency.Registered ? "registered" : "FAILED " + _emergency.Win32Error)}", "", "Window", $"  opacity: {_window.Opacity:P0}", $"  TopMost: {_window.TopMost}", $"  click-through: {_window.ClickThrough}", $"  hidden: {_window.Hidden}", "", "Screenshot", $"  last: {_capture.Detail}", "", "Paste", $"  stage: {_paste.LastResult.Stage}", $"  method: {_paste.LastResult.Method}", $"  detail: {_paste.LastResult.Detail}", "", "Voice", $"  native shortcut: {_voice.Status.Shortcut}", $"  last: {_voice.Status.LastInvocation}", $"  fallback: {_voice.Status.FallbackState}", "", "Audio", $"  Right Ctrl enabled: {_settings.RightCtrlAudioEnabled}", $"  status: {_audio?.Status ?? "Not started"}", $"  microphone: {_audio?.Microphone ?? "Not started"}", $"  loopback: {_audio?.Loopback ?? "Not started"}", $"  virtual output: {_audio?.VirtualOutput ?? "Not started"}", "  endpoint safety: Windows defaults are never selected or changed.", "", "Recovery", $"  journal exists: {_recovery.HasPendingSnapshot}", $"  path: {_recovery.JournalPath}"]);
        DetailsText.Text = string.Join(Environment.NewLine, lines);
    }

    private void AttachClick(object sender, RoutedEventArgs e) { Attach(); RefreshDiagnostics(); }
    private void DiagnosticsClick(object sender, RoutedEventArgs e) { _voice.Probe(_window.Target); _privacy.ScanNow(); _privacyTransitions.NotifyVisibilityOrLifecycleTransition(); RefreshDiagnostics(); }
    private void SettingsClick(object sender, RoutedEventArgs e) { var dialog = new SettingsWindow(_settings, _audioDevices ??= new AudioDeviceService()) { Owner = this }; if (dialog.ShowDialog() == true) { TryAutoLaunch(); RefreshDiagnostics(); } }
    private void RestoreClick(object sender, RoutedEventArgs e) { _window.Restore(); RefreshDiagnostics(); }
    private void ExitClick(object sender, RoutedEventArgs e) => ExitSafely();
    private void ShowController() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void RefreshAndShowDiagnostics() { _voice.Probe(_window.Target); _privacy.ScanNow(); _privacyTransitions.NotifyVisibilityOrLifecycleTransition(); RefreshDiagnostics(); ShowController(); }
    private void ExitSafely() { Interlocked.Exchange(ref _shutdownStarted, 1); Close(); }
    protected override void OnClosed(EventArgs e) { Interlocked.Exchange(ref _shutdownStarted, 1); _reacquireTimer.Stop(); _privacyTransitions.Dispose(); _privacy.Dispose(); lock (_audioGate) { _audio?.Dispose(); _audioDevices?.Dispose(); } _tray.Dispose(); _hotkeys.Dispose(); _emergency.Dispose(); _window.Dispose(); base.OnClosed(e); }
}
