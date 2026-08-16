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
            Shutdown(); return;
        }
        AppPaths.Ensure();
        var log = new AppLogger();
        new RecoveryService(log).TryRecoverStaleState();
        new MainWindow(log).Show();
    }
    protected override void OnExit(ExitEventArgs e) { _singleInstance?.Dispose(); base.OnExit(e); }
}
