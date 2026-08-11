using System.Windows;

namespace QwenWorkOverlay;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = new SettingsService();
        settings.Load();
        var logger = new AppLogger();
        logger.Info("Startup");
        MainWindow = new MainWindow(settings, logger);
        MainWindow.Show();
    }
}
