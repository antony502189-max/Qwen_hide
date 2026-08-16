namespace ChatGPTDesktopController;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();
        var log = new AppLogger();
        new RecoveryService(log).TryRecoverStaleState();
        new MainWindow(log).Show();
    }
}
