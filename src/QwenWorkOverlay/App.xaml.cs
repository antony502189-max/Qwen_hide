using System.Threading;
using System.Windows;

namespace QwenWorkOverlay;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\QwenDesktopController.Singleton", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Qwen Desktop Controller is already running. Check the system tray.",
                "Qwen Desktop Controller",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settings = new SettingsService();
        settings.Load();
        _logger = new AppLogger();
        _logger.Info("Startup");

        // If a previous controller process was killed while Qwen remained alive, restore the exact
        // native window styles before attaching again. This prevents persistent click-through/alpha state.
        new WindowRecoveryService(_logger).TryRecoverStaleState();

        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error("Unhandled dispatcher exception: " + args.Exception.GetType().Name);
            try { (MainWindow as QwenWorkOverlay.MainWindow)?.EmergencyRestoreForCrash(); } catch { }
            // Keep the default crash behavior after the emergency restore attempt.
            args.Handled = false;
        };

        SessionEnding += (_, _) =>
        {
            try { (MainWindow as QwenWorkOverlay.MainWindow)?.EmergencyRestoreForCrash(); } catch { }
        };

        MainWindow = new MainWindow(settings, _logger);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch { }
        _singleInstance?.Dispose();
        _logger?.Info("Application exit");
        base.OnExit(e);
    }
}
