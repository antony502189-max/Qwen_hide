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
    private Forms.NotifyIcon? _tray;
    private bool _resourcesDisposed;
    private bool _privacyProbeRunning;
    private bool _voiceToggleStartedByHotkey;

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
        _hotkeys = new GlobalHotkeys(HandleHotkey, _log, _emergency.RequestExit);
        _hotkeys.RightCtrlChanged += RightCtrlChanged;
        _hotkeys.VoiceToggleChanged += VoiceToggleChanged;
        if (!_hotkeys.AllRegistered) StatusText.Text = "Some global hotkeys are unavailable: " + _hotkeys.FailureSummary;
        if (_options.SafeMode) StatusText.Text = "SAFE MODE: observational attach only; mutations, audio and screenshots are disabled.";
        _sessionMonitor.Start();
        UpdateStatus();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Controller", null, (_, _) => QueueUi(ShowController));
        menu.Items.Add("Show Qwen", null, (_, _) => QueueUi(ShowQwen));
        menu.Items.Add("Diagnostics", null, (_, _) => QueueUi(ShowDiagnostics));
        menu.Items.Add("Emergency Restore", null, (_, _) => _emergency.RequestExit());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Controller", null, (_, _) => _emergency.RequestExit());
        _tray = new Forms.NotifyIcon { Text = "Qwen Desktop Controller", Icon = System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
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
                    NativeStatusText.Text = "Observationally attached to the installed Qwen Desktop";
                    NativeDetailsText.Text = $"{target.Summary}\n{target.ExecutablePath}\nClass: {target.WindowClass}";
                    _log.Info("Native Qwen attached observationally");
                }
            }
            else if (target is null && !_qwen.IsAttached)
            {
                NativeStatusText.Text = "Installed Qwen Desktop is not attached";
                NativeDetailsText.Text = "Open Qwen normally, or use Attach / Open Qwen. No window state is changed while attaching.";
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
            case 1: if (!_options.SafeMode && !_qwen.ToggleVisibility()) Toast("Qwen is not attached"); break;
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
        if (_options.SafeMode) { Toast("SAFE MODE blocks Qwen mutation"); return false; }
        if (!_qwen.IsAttached) { Toast("Qwen is not attached"); return false; }
        return true;
    }

    private void VoiceToggleChanged(bool active) => QueueUi(() =>
    {
        if (_options.SafeMode) { Toast("SAFE MODE blocks voice automation"); return; }
        var hwnd = _qwen.Target?.Hwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) { _sessionMonitor.CheckNow(); Toast("Qwen is not attached"); return; }
        _ = Task.Run(() => _voice.TryInvokeVoiceButton(hwnd)).ContinueWith(t => QueueUi(() =>
        {
            _voiceToggleStartedByHotkey = active && t.Status == TaskStatus.RanToCompletion && t.Result;
            Toast(_voiceToggleStartedByHotkey ? "Qwen voice recording toggled" : _voice.State);
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
        if (_qwen.SetClickThrough(requested)) { _settings.Current.ClickThrough = requested; _settings.Save(); Toast($"Click-through {(requested ? "ON" : "OFF")}"); }
        else Toast("Click-through failed");
    }

    private void ToggleTopMost()
    {
        var requested = !_qwen.TopMost;
        if (_qwen.SetTopMost(requested)) { _settings.Current.TopMost = requested; _settings.Save(); Toast($"Qwen TopMost {(requested ? "ON" : "OFF")}"); }
        else Toast("TopMost change failed");
    }

    private void SetOpacity(double value)
    {
        value = Math.Clamp(value, .35, 1);
        if (value < .999 && !_options.OpacityEnabled)
        {
            Toast("Opacity is disabled until this Qwen build passes a target-machine compositor test (--enable-opacity)");
            return;
        }
        if (_qwen.SetOpacity(value)) { _settings.Current.Opacity = value; _settings.Save(); Toast($"Qwen opacity {value:P0}"); }
        else Toast("Opacity change failed or unsupported by Qwen compositor");
    }

    private async Task PasteClipboardIntoQwenAsync()
    {
        if (_options.SafeMode) { Toast("SAFE MODE blocks paste"); return; }
        if (!_qwen.ShowAndActivate()) { Toast("Qwen is not attached"); return; }
        await Task.Delay(140);
        try { Forms.SendKeys.SendWait("^v"); Toast("Clipboard pasted into Qwen"); } catch { Toast("Clipboard paste failed; use Ctrl+V manually"); }
    }

    private async Task CaptureWorkWindowAsync()
    {
        if (_options.SafeMode) { Toast("SAFE MODE blocks screenshots"); return; }
        var hwnd = _foregroundTracker.LastNonQwenWindow;
        var result = hwnd != IntPtr.Zero && await StaWork.RunAsync(() => ScreenshotService.CaptureWindowToClipboard(hwnd, _qwen.Target?.Hwnd ?? IntPtr.Zero));
        Toast(result ? "Screenshot copied" : "Screenshot failed");
    }

    private async Task CaptureMonitorAsync()
    {
        if (_options.SafeMode) { Toast("SAFE MODE blocks screenshots"); return; }
        var result = await StaWork.RunAsync(() => ScreenshotService.CaptureMonitorToClipboard(_qwen.Target?.Hwnd ?? IntPtr.Zero));
        Toast(result ? "Monitor screenshot copied" : "Screenshot failed");
    }

    private void UpdateStatus()
    {
        if (_qwen.IsAttached && _qwen.Target is { } target)
        {
            NativeStatusText.Text = "Observationally attached to the installed Qwen Desktop";
            NativeDetailsText.Text = $"{target.Summary} · {target.WindowTitle}";
            WindowStatusText.Text = $"Opacity {_qwen.Opacity:P0} · TopMost {(_qwen.TopMost ? "ON" : "OFF")} · Click-through {(_qwen.ClickThrough ? "ON" : "OFF")}";
        }
        else WindowStatusText.Text = "Window controls: waiting for the installed Qwen Desktop";
        var audio = _audio;
        AudioStatusText.Text = audio is null ? "Audio mix: disabled until explicitly enabled" : audio.Running ? "Audio mix: " + audio.InjectionState : "Audio mix: idle";
        PrivacyStatusText.Text = "Capture privacy: " + _qwen.PrivacyStatus;
    }

    private void Toast(string text)
    {
        StatusText.Text = text;
        if (!IsVisible && _tray is not null) { _tray.BalloonTipTitle = "Qwen Desktop Controller"; _tray.BalloonTipText = text; _tray.ShowBalloonTip(1200); }
    }

    private async void ShowDiagnostics()
    {
        var target = _qwen.Target;
        var window = new Window { Title = "Qwen Desktop Controller Diagnostics", Width = 620, Height = 640, Owner = this, Content = new System.Windows.Controls.TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Text = BuildCachedDiagnostics(target) } };
        window.Show();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var metrics = await _diagnostics.CollectAsync(target, timeout.Token);
            if (window.Content is System.Windows.Controls.TextBox box && window.IsVisible) box.Text = BuildCachedDiagnostics(target) + "\n\n" + FormatProcess("Controller", metrics.Controller) + "\n" + FormatProcess("Qwen", metrics.Qwen) + $"\nDispatcher latency: UI updates are queued (no synchronous Dispatcher.Invoke)\nLast keyboard-hook callback: {_hotkeys?.LastHookDuration.TotalMilliseconds:F3} ms\nAsync log messages dropped: {_log.DroppedMessageCount}";
        }
        catch (Exception ex) { _log.Error("Diagnostics collection failed: " + ex.GetType().Name); }
    }

    private string BuildCachedDiagnostics(QwenTarget? target) =>
        $"Controller version: {GetType().Assembly.GetName().Version}\nController PID: {Environment.ProcessId}\nSafe mode: {_options.SafeMode}\nQwen attached: {_qwen.IsAttached}\nQwen PID: {target?.ProcessId}\nQwen HWND: {FormatHwnd(target?.Hwnd ?? IntPtr.Zero)}\nQwen class: {target?.WindowClass}\nQwen executable: {target?.ExecutablePath}\nAttach state: observational\nOpacity: {_qwen.Opacity:P0}\nTopMost: {_qwen.TopMost}\nClick-through: {_qwen.ClickThrough}\nPrivacy host: {_qwen.PrivacyStatus}\nWDA requested/verified: 0x{_qwen.RequestedAffinity:X}/0x{_qwen.VerifiedAffinity:X}\nHotkeys: {_hotkeys?.FailureSummary ?? "initializing"}\nRight Ctrl hook: {(_hotkeys?.HookReady == true ? "READY" : "FAILED")}\nAudio: {(_audio?.Running == true ? "running" : "disabled/idle")}\nRecovery journal: {_recovery.JournalPath}\nLog: {_log.LogPath}\n\nCollecting process metrics asynchronously…";

    private static string FormatProcess(string label, ProcessDiagnostics value) => $"{label}: PID={value.Pid}, CPU={value.CpuPercent:F2}%, Working set={value.WorkingSetBytes / 1024d / 1024d:F1} MiB, Threads={value.ThreadCount}, Handles={value.HandleCount}, State={value.State}";
    private static string FormatHwnd(IntPtr hwnd) => hwnd == IntPtr.Zero ? "n/a" : $"0x{hwnd.ToInt64():X}";

    private void ShowController() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ShowQwen() { if (!_qwen.ShowAndActivate()) { _sessionMonitor.CheckNow(); Toast("Qwen is not attached"); } }
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(() =>
        {
            var path = _locator.FindInstalledExecutable(_settings.Current.QwenExecutablePath);
            if (!string.IsNullOrWhiteSpace(path) && _settings.Current.AutoLaunchQwen) _locator.TryLaunch(path);
            _sessionMonitor.CheckNow();
        });
        Toast("Looking for the installed Qwen Desktop…");
    }
    private void ShowQwen_Click(object sender, RoutedEventArgs e) => ShowQwen();
    private void Click_Click(object sender, RoutedEventArgs e) { if (RequireMutation()) ToggleClickThrough(); }
    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        if (!_options.ExperimentalPrivacyHostEnabled)
        {
            Toast("Privacy host is disabled pending staged target-machine validation (--enable-experimental-privacy-host)");
            return;
        }
        if (!RequireMutation()) return;
        var enabled = _qwen.PrivacyState == CapturePrivacyState.Enabled;
        var ok = enabled ? _qwen.DisablePrivacyHost() : _qwen.EnablePrivacyHost();
        Toast(ok ? (enabled ? "Privacy host OFF; Qwen restored" : _qwen.PrivacyStatus) : _qwen.PrivacyStatus);
        UpdateStatus();
    }
    private void PrivacyGdiProbe_Click(object sender, RoutedEventArgs e) { if (_options.SafeMode) return; _ = Task.Run(() => _qwen.ValidatePrivacyGdiCapture()).ContinueWith(t => QueueUi(() => { Toast("GDI capture probe: " + (t.Status == TaskStatus.RanToCompletion ? t.Result.Verdict : CaptureProbeVerdict.Failed)); UpdateStatus(); }), TaskScheduler.Default); }
    private void PrivacyPrintWindowProbe_Click(object sender, RoutedEventArgs e) { if (_options.SafeMode) return; _ = Task.Run(() => _qwen.ValidatePrivacyPrintWindowCapture()).ContinueWith(t => QueueUi(() => { Toast("PrintWindow capture probe: " + (t.Status == TaskStatus.RanToCompletion ? t.Result.Verdict : CaptureProbeVerdict.Failed)); UpdateStatus(); }), TaskScheduler.Default); }
    private async void PrivacyNativeProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_options.SafeMode || _privacyProbeRunning) return;
        _privacyProbeRunning = true;
        try { var results = await _qwen.ValidatePrivacyNativeCapturePathsAsync().ConfigureAwait(true); Toast($"Native probes: Desktop Duplication={results.DesktopDuplication.Verdict}; WGC={results.WindowsGraphicsCapture.Verdict}"); }
        catch { Toast("Native privacy probes failed"); }
        finally { _privacyProbeRunning = false; UpdateStatus(); }
    }
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
        lock (_audioGate)
        {
            try { _audio?.Stop(); } catch { }
        }
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
