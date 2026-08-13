using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace QwenWorkOverlay;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly AppLogger _log;
    private readonly AudioDeviceService _devices = new();
    private readonly AudioDefaultDeviceGuard _deviceGuard;
    private readonly MixedAudioSession _audio;
    private readonly QwenProcessLocator _locator;
    private readonly QwenWindowController _qwen;
    private readonly QwenVoiceAutomation _voice;
    private readonly ForegroundWindowTracker _foregroundTracker;
    private readonly DispatcherTimer _attachTimer;

    private GlobalHotkeys? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private bool _allowExit;
    private bool _launchAttempted;
    private bool _voiceStartedByHotkey;
    private bool _voiceToggleStartedByHotkey;
    private bool _privacyProbeRunning;
    private bool _resourcesDisposed;

    public MainWindow(SettingsService settings, AppLogger log)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _deviceGuard = new AudioDefaultDeviceGuard(_devices);
        _audio = new MixedAudioSession(_devices, _log);
        _locator = new QwenProcessLocator(log);
        _qwen = new QwenWindowController(log);
        _voice = new QwenVoiceAutomation(log);
        _foregroundTracker = new ForegroundWindowTracker(() => _qwen.Target?.Hwnd);
        _attachTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _attachTimer.Tick += (_, _) => EnsureAttached(allowLaunch: false);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var pos = WindowStateNormalizer.Normalize(
            _settings.Current.ControllerX,
            _settings.Current.ControllerY,
            Width,
            Height,
            Forms.Screen.AllScreens);
        Left = pos.X;
        Top = pos.Y;

        CreateTrayIcon();
        _hotkeys = new GlobalHotkeys(HandleHotkey, _log);
        _hotkeys.RightCtrlChanged += RightCtrlChanged;
        _hotkeys.VoiceToggleChanged += VoiceToggleChanged;
        if (!_hotkeys.AllRegistered)
            StatusText.Text = "Some global hotkeys are unavailable: " + _hotkeys.FailureSummary;

        EnsureAttached(allowLaunch: true);
        _attachTimer.Start();
        UpdateStatus();

        if (_settings.Current.StartControllerInTray && _qwen.IsAttached)
            Dispatcher.BeginInvoke((Action)Hide, DispatcherPriority.ApplicationIdle);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open controller", null, (_, _) => Dispatcher.Invoke(ShowController));
        menu.Items.Add("Show Qwen", null, (_, _) => Dispatcher.Invoke(() => _qwen.ShowAndActivate()));
        menu.Items.Add("Toggle click-through", null, (_, _) => Dispatcher.Invoke(ToggleClickThrough));
        menu.Items.Add("Diagnostics", null, (_, _) => Dispatcher.Invoke(ShowDiagnostics));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit controller (restore Qwen)", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _tray = new Forms.NotifyIcon
        {
            Text = "Qwen Desktop Controller",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowController);
    }

    private void EnsureAttached(bool allowLaunch)
    {
        if (_qwen.IsAttached)
        {
            UpdateStatus();
            return;
        }

        if (_qwen.Target is not null)
            _qwen.Detach(restore: false);

        var target = _locator.FindRunningTarget();
        if (target is null && allowLaunch && _settings.Current.AutoLaunchQwen && !_launchAttempted)
        {
            _launchAttempted = true;
            var executable = _locator.FindInstalledExecutable(_settings.Current.QwenExecutablePath);
            if (!string.IsNullOrWhiteSpace(executable))
            {
                _settings.Current.QwenExecutablePath = executable;
                _settings.Save();
                if (_locator.TryLaunch(executable))
                {
                    NativeStatusText.Text = "Launching the installed Qwen Desktop…";
                    NativeDetailsText.Text = executable;
                    return;
                }
            }
        }

        target ??= _locator.FindRunningTarget();
        if (target is null)
        {
            NativeStatusText.Text = "Installed Qwen Desktop is not attached";
            NativeDetailsText.Text = "Open Qwen normally, or choose Qwen.exe in Settings. No embedded qwen.ai window will be created.";
            UpdateStatus();
            return;
        }

        if (_qwen.Attach(target, _settings.Current.Opacity, _settings.Current.TopMost))
        {
            if (!string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                _settings.Current.QwenExecutablePath = target.ExecutablePath;
                _settings.Save();
            }
            _voice.Probe(target.Hwnd);
            NativeStatusText.Text = "Attached to the installed Qwen Desktop";
            NativeDetailsText.Text = $"{target.Summary}\n{target.ExecutablePath ?? "Executable path unavailable"}\nClass: {target.WindowClass}";
            _log.Info("Native Qwen attached successfully");
        }
        else
        {
            NativeStatusText.Text = "Qwen was detected but the controller could not safely attach";
            NativeDetailsText.Text = "Check Diagnostics/logs. If Qwen runs elevated, run the controller at the same integrity level.";
        }
        UpdateStatus();
    }

    private void HandleHotkey(int id)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            EnsureAttached(allowLaunch: false);
            switch (id)
            {
                case 1:
                    if (!_qwen.ToggleVisibility()) Toast("Qwen is not attached");
                    break;
                case 2:
                    ToggleClickThrough();
                    break;
                case 3:
                    ToggleTopMost();
                    break;
                case 4:
                    Toast(_qwen.PrivacyStatus);
                    break;
                case 5:
                    SetOpacity(_settings.Current.Opacity + .05);
                    break;
                case 6:
                    SetOpacity(_settings.Current.Opacity - .05);
                    break;
                case 7:
                    await PasteClipboardIntoQwenAsync();
                    break;
                case 8:
                    ShowDiagnostics();
                    break;
                case 9:
                {
                    var hwnd = _foregroundTracker.LastNonQwenWindow;
                    var qwenHwnd = _qwen.Target?.Hwnd ?? IntPtr.Zero;
                    var ok = hwnd != IntPtr.Zero && ScreenshotService.CaptureWindowToClipboard(hwnd, qwenHwnd);
                    Toast(ok ? "Screenshot copied" : "Screenshot failed");
                    break;
                }
                case 10:
                {
                    var qwenHwnd = _qwen.Target?.Hwnd ?? IntPtr.Zero;
                    var ok = ScreenshotService.CaptureMonitorToClipboard(qwenHwnd);
                    Toast(ok ? "Monitor screenshot copied" : "Screenshot failed");
                    break;
                }
                case 11:
                    _log.Info("Emergency restore-and-exit hotkey invoked");
                    ExitApplication();
                    return;
            }
            UpdateStatus();
        });
    }

    private void VoiceToggleChanged(bool active)
    {
        Dispatcher.InvokeAsync(() =>
        {
            EnsureAttached(allowLaunch: false);
            if (_qwen.Target is null)
            {
                _voiceToggleStartedByHotkey = false;
                Toast("Qwen is not attached");
                return;
            }

            if (active)
            {
                _voiceToggleStartedByHotkey = _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);
                Toast(_voiceToggleStartedByHotkey ? "Qwen voice recording toggled ON" : _voice.State);
            }
            else
            {
                if (_voiceToggleStartedByHotkey)
                {
                    var stopped = _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);
                    Toast(stopped ? "Qwen voice recording toggled OFF" : _voice.State);
                }
                else
                {
                    Toast("Qwen voice toggle was not started by Controller");
                }
                _voiceToggleStartedByHotkey = false;
            }

            _log.Info("Ctrl+Shift+R voice toggle active=" + active + "; state=" + _voice.State);
            UpdateStatus();
        });
    }

    private void RightCtrlChanged(bool down)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_settings.Current.RightCtrlAudioEnabled) return;

            if (down)
            {
                _audio.Start(
                    _settings.Current.MicrophoneDeviceId,
                    _settings.Current.LoopbackDeviceId,
                    _settings.Current.VirtualMixOutputDeviceId,
                    _settings.Current.MicGain,
                    _settings.Current.SystemGain);

                _voiceStartedByHotkey = false;
                if (_audio.VirtualOutputReady && _settings.Current.AutoToggleQwenVoiceWithRightCtrl && _qwen.Target is not null)
                    _voiceStartedByHotkey = _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);

                var suffix = _voiceStartedByHotkey ? " · Qwen voice toggled ON" : string.Empty;
                Toast((_audio.VirtualOutputReady ? "Qwen audio mix ON" : _audio.InjectionState) + suffix);
                _log.Info("Audio mix key down: " + _audio.InjectionState + "; voice=" + _voice.State);
            }
            else
            {
                if (_voiceStartedByHotkey && _qwen.Target is not null)
                    _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);
                _voiceStartedByHotkey = false;
                _audio.Stop();
                Toast("Qwen audio mix OFF");
                _log.Info("Audio mix key up; voice=" + _voice.State);
            }
            UpdateStatus();
        });
    }

    private void ToggleClickThrough()
    {
        if (!_qwen.IsAttached)
        {
            Toast("Qwen is not attached");
            return;
        }
        var requested = !_qwen.ClickThrough;
        Toast(_qwen.SetClickThrough(requested)
            ? $"Click-through {(requested ? "ON" : "OFF")}"
            : "Click-through failed");
        UpdateStatus();
    }

    private void ToggleTopMost()
    {
        if (!_qwen.IsAttached)
        {
            Toast("Qwen is not attached");
            return;
        }
        var requested = !_qwen.TopMost;
        if (_qwen.SetTopMost(requested))
        {
            _settings.Current.TopMost = requested;
            _settings.Save();
            Toast($"Qwen TopMost {(requested ? "ON" : "OFF")}");
        }
        else Toast("TopMost change failed");
        UpdateStatus();
    }

    private void SetOpacity(double value)
    {
        value = Math.Clamp(value, .35, 1.0);
        if (!_qwen.IsAttached)
        {
            _settings.Current.Opacity = value;
            _settings.Save();
            Toast($"Opacity saved: {Math.Round(value * 100)}%");
            return;
        }
        if (_qwen.SetOpacity(value))
        {
            _settings.Current.Opacity = value;
            _settings.Save();
            Toast($"Qwen opacity {Math.Round(value * 100)}%");
        }
        else Toast("Opacity change failed");
        UpdateStatus();
    }

    private async Task PasteClipboardIntoQwenAsync()
    {
        if (!_qwen.ShowAndActivate())
        {
            Toast("Qwen is not attached");
            return;
        }

        await Task.Delay(140);
        try
        {
            Forms.SendKeys.SendWait("^v");
            Toast("Clipboard pasted into Qwen");
        }
        catch (Exception ex)
        {
            _log.Error("Clipboard paste failed: " + ex.GetType().Name);
            Toast("Clipboard paste failed; use Ctrl+V manually");
        }
    }

    private void UpdateStatus()
    {
        if (_qwen.IsAttached && _qwen.Target is not null)
        {
            var target = _qwen.Target;
            NativeStatusText.Text = "Attached to the installed Qwen Desktop";
            NativeDetailsText.Text = $"{target.Summary} · {target.WindowTitle}";
            WindowStatusText.Text = $"Opacity {Math.Round(_qwen.Opacity * 100)}% · TopMost {(_qwen.TopMost ? "ON" : "OFF")} · Click-through {(_qwen.ClickThrough ? "ON" : "OFF")}";
        }
        else
        {
            WindowStatusText.Text = "Window controls: waiting for the installed Qwen Desktop";
        }

        AudioStatusText.Text = _audio.Running
            ? $"Audio mix: {_audio.InjectionState} · mic={_audio.MicrophoneState} · system={_audio.LoopbackState}"
            : $"Audio mix: idle · mic={_audio.MicrophoneState} · system={_audio.LoopbackState} · voice={_voice.State}";
        PrivacyStatusText.Text = "Capture privacy: " + _qwen.PrivacyStatus;
    }

    private void Toast(string text)
    {
        StatusText.Text = text;
        if (!IsVisible && _tray is not null)
        {
            _tray.BalloonTipTitle = "Qwen Desktop Controller";
            _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(1200);
        }
    }

    private void ShowDiagnostics()
    {
        EnsureAttached(allowLaunch: false);
        var target = _qwen.Target;
        if (target is not null) _voice.Probe(target.Hwnd);
        var defaultsSafe = _deviceGuard.Verify(_devices);
        var currentDefaultInput = _devices.DefaultInput();
        var currentCommunicationsInput = _devices.DefaultCommunicationsInput();
        var hotkeyState = _hotkeys is null ? "not initialized" : _hotkeys.FailureSummary;
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
        var controllerHwnd = new WindowInteropHelper(this).Handle;
        var controllerDpi = controllerHwnd == IntPtr.Zero ? 0 : Native.GetDpiForWindow(controllerHwnd);
        var controllerDpiAwareness = controllerHwnd == IntPtr.Zero
            ? "Unavailable"
            : Native.DescribeDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(controllerHwnd));

        var text =
            $"Controller version: {version}\n" +
            $"Native Qwen attached: {_qwen.IsAttached}\n" +
            $"Executable: {target?.ExecutablePath ?? "n/a"}\n" +
            $"PID: {target?.ProcessId.ToString() ?? "n/a"}\n" +
            $"Process start UTC ticks: {target?.ProcessStartUtcTicks.ToString() ?? "n/a"}\n" +
            $"HWND: {(target is null ? "n/a" : $"0x{target.Hwnd.ToInt64():X}")}\n" +
            $"Window class: {target?.WindowClass ?? "n/a"}\n" +
            $"Transparency: {_qwen.Opacity:P0}\n" +
            $"TopMost: {_qwen.TopMost}\n" +
            $"Click-through: {_qwen.ClickThrough}\n\n" +
            $"Global hotkeys: {hotkeyState}\n" +
            $"Right Ctrl hook: {(_hotkeys?.HookReady == true ? "READY" : "FAILED")}\n" +
            $"Ctrl+Shift+R: independent Qwen voice toggle\n\n" +
            $"Physical microphone: {_audio.MicrophoneState}\n" +
            $"System loopback: {_audio.LoopbackState}\n" +
            $"Virtual mix output: {_audio.VirtualOutputState}\n" +
            $"Mixer: {(_audio.Running ? "RUNNING" : "IDLE")}\n" +
            $"Mic bytes: {_audio.MicrophoneBytes}\n" +
            $"Loopback bytes: {_audio.LoopbackBytes}\n" +
            $"Mixed frames: {_audio.MixedFrames}\n" +
            $"Qwen audio target: {_audio.InjectionState}\n" +
            $"Qwen voice automation: {_voice.State}\n" +
            $"Matched voice control: {_voice.LastMatchedButton ?? "n/a"}\n" +
            $"Voice click fallback: {_voice.ClickFallbackStatus}\n\n" +
            $"Privacy mode: {_qwen.PrivacyStatus}\n" +
            $"Privacy host HWND: {FormatHwnd(_qwen.PrivacyHostHwnd)}\n" +
            $"Qwen child HWND: {FormatHwnd(target?.Hwnd ?? IntPtr.Zero)}\n" +
            $"Qwen original parent: {FormatHwnd(_qwen.OriginalParent)}\n" +
            $"Qwen current parent: {FormatHwnd(_qwen.CurrentParent)}\n" +
            $"WDA requested: 0x{_qwen.RequestedAffinity:X}\n" +
            $"WDA verified: 0x{_qwen.VerifiedAffinity:X}\n" +
            $"DWM composition: {_qwen.DwmCompositionEnabled}\n" +
            $"Controller DPI: {controllerDpi}\n" +
            $"Controller DPI awareness: {controllerDpiAwareness}\n" +
            $"Host DPI: {_qwen.HostDpi}\n" +
            $"Qwen DPI: {_qwen.QwenDpi}\n" +
            $"Host DPI awareness: {Native.DescribeDpiAwarenessContext(_qwen.HostDpiAwarenessContext)}\n" +
            $"Qwen DPI awareness: {Native.DescribeDpiAwarenessContext(_qwen.QwenDpiAwarenessContext)}\n" +
            $"GDI screen-copy probe: {_qwen.GdiCaptureProbe.Verdict}\n" +
            $"GDI probe detail: {_qwen.GdiCaptureProbe.Detail}\n" +
            $"GDI mean RGB difference: {_qwen.GdiCaptureProbe.MeanRgbDifference:F1}\n" +
            $"Desktop Duplication probe: {_qwen.DesktopDuplicationCaptureProbe.Verdict}\n" +
            $"Desktop Duplication detail: {_qwen.DesktopDuplicationCaptureProbe.Detail}\n" +
            $"Windows Graphics Capture probe: {_qwen.WindowsGraphicsCaptureProbe.Verdict}\n" +
            $"Windows Graphics Capture detail: {_qwen.WindowsGraphicsCaptureProbe.Detail}\n" +
            $"Recovery state: {(_qwen.PrivacyState == CapturePrivacyState.Enabled ? "privacy-host active" : "native window journal / normal")}\n" +
            $"Windows audio defaults unchanged: {defaultsSafe}\n" +
            $"Default input before: {_deviceGuard.InputBefore}\n" +
            $"Default input current: {currentDefaultInput}\n" +
            $"Default communications before: {_deviceGuard.CommunicationsBefore}\n" +
            $"Default communications current: {currentCommunicationsInput}\n" +
            $"Log: {_log.LogPath}";

        MessageBox.Show(this, text, "Qwen Desktop Controller Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowController()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        _launchAttempted = false;
        EnsureAttached(allowLaunch: true);
    }

    private void ShowQwen_Click(object sender, RoutedEventArgs e)
    {
        EnsureAttached(allowLaunch: false);
        if (!_qwen.ShowAndActivate()) Toast("Qwen is not attached");
    }

    private void Click_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();
    private void Privacy_Click(object sender, RoutedEventArgs e) => TogglePrivacyHost();
    private void PrivacyGdiProbe_Click(object sender, RoutedEventArgs e)
    {
        var result = _qwen.ValidatePrivacyGdiCapture();
        Toast("GDI capture probe: " + result.Verdict);
        UpdateStatus();
    }
    private async void PrivacyNativeProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_privacyProbeRunning)
        {
            Toast("Native capture validation is already running");
            return;
        }

        _privacyProbeRunning = true;
        try
        {
            var results = await _qwen.ValidatePrivacyNativeCapturePathsAsync();
            Toast($"Native capture APIs: Desktop Duplication {results.DesktopDuplication.Verdict}; WGC {results.WindowsGraphicsCapture.Verdict}");
        }
        finally
        {
            _privacyProbeRunning = false;
            UpdateStatus();
        }
    }
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => ShowDiagnostics();

    private void TogglePrivacyHost()
    {
        EnsureAttached(allowLaunch: false);
        if (!_qwen.IsAttached)
        {
            Toast("Qwen is not attached");
            return;
        }
        var enabled = _qwen.PrivacyState == CapturePrivacyState.Enabled;
        var ok = enabled ? _qwen.DisablePrivacyHost() : _qwen.EnablePrivacyHost();
        Toast(ok
            ? (enabled ? "Privacy host OFF; Qwen restored" : "Privacy host ON; WDA verified")
            : _qwen.PrivacyStatus);
        UpdateStatus();
    }

    private static string FormatHwnd(IntPtr hwnd) => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _devices) { Owner = this };
        dialog.ShowDialog();
        EnsureAttached(allowLaunch: false);
        if (_qwen.IsAttached)
        {
            _qwen.SetOpacity(_settings.Current.Opacity);
            _qwen.SetTopMost(_settings.Current.TopMost);
        }
        UpdateStatus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
    }

    public void EmergencyRestoreForCrash()
    {
        _allowExit = true;
        DisposeRuntimeResources(saveControllerPosition: false);
    }

    private void DisposeRuntimeResources(bool saveControllerPosition)
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;

        _attachTimer.Stop();
        try
        {
            if (_voiceStartedByHotkey && _qwen.Target is not null)
                _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);
        }
        catch { }
        _voiceStartedByHotkey = false;

        try
        {
            if (_voiceToggleStartedByHotkey && _qwen.Target is not null)
                _voice.TryInvokeVoiceButton(_qwen.Target.Hwnd);
        }
        catch { }
        _voiceToggleStartedByHotkey = false;

        try { _audio.Stop(); } catch { }
        try { _qwen.Dispose(); } catch { }
        try { _foregroundTracker.Dispose(); } catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }

        if (saveControllerPosition)
        {
            _settings.Current.ControllerX = Left;
            _settings.Current.ControllerY = Top;
            try { _settings.Save(); } catch { }
        }

        var defaultsSafe = false;
        try
        {
            using var verifier = new AudioDeviceService();
            defaultsSafe = _deviceGuard.Verify(verifier);
        }
        catch { }

        try { _devices.Dispose(); } catch { }
        _log.Info("Shutdown/recovery cleanup; Windows audio defaults unchanged=" + defaultsSafe);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        DisposeRuntimeResources(saveControllerPosition: true);
        _log.Info("Shutdown complete");
    }
}
