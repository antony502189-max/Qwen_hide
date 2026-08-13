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
                "Контроллёр Qwen Desktop уже запущен. Проверьте системный трей.",
                "Контроллёр Qwen Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var options = ControllerRuntimeOptions.FromArguments(e.Args);
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

        MainWindow = new MainWindow(settings, _logger, options);
        MainWindow.Show();
        // Test harness only: bounded lifetime is accepted exclusively with safe mode, which has
        // no Qwen mutation or lazily-created audio resources to recover.
        if (options.SafeMode && options.ExitAfterSeconds is > 0)
            _ = Task.Delay(TimeSpan.FromSeconds(options.ExitAfterSeconds.Value)).ContinueWith(_ => Environment.Exit(0));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch { }
        _singleInstance?.Dispose();
        _logger?.Info("Application exit");
        _logger?.Dispose();
        base.OnExit(e);
    }
}
