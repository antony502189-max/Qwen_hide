namespace ChatGPTDesktopController;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\ChatGPTDesktopController-2D5478B4-0F1B-4D04-A123-5AAB41C99D95", out var firstInstance);
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show("ChatGPT Classic Controller is already running.", "ChatGPT Classic Controller", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.Ensure();
        var log = new AppLogger();
        new RecoveryService(log).TryRecoverStaleState();

        // Keep the controller completely invisible at startup, like the Qwen controller.
        // The diagnostics window is shown only on demand (Ctrl+Alt+D / tray action).
        var controller = new MainWindow(log);
        MainWindow = controller;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
