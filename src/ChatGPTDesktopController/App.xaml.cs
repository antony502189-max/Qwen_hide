namespace ChatGPTDesktopController;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private PrivacyOnlyController? _privacyOnly;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--stop", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(StopRunningController());
            return;
        }
        var privacyOnly = e.Args.Contains("--privacy-only", StringComparer.OrdinalIgnoreCase);
        _singleInstance = new Mutex(true, "Local\\ChatGPTCurrentController-Full-v2", out var firstInstance);
        if (!firstInstance)
        {
            Shutdown();
            return;
        }

        AppPaths.Ensure();
        var log = new AppLogger();
        log.Info(privacyOnly ? "Starting current ChatGPT privacy-only mode" : "Starting full current ChatGPT controller with Classic hotkeys and features");
        if (privacyOnly)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _privacyOnly = new PrivacyOnlyController(log);
            return;
        }
        new RecoveryService(log).TryRecoverStaleState();

        // Keep the controller completely invisible at startup, like the Qwen controller.
        // The diagnostics window is shown only on demand (Ctrl+Alt+D / tray action).
        var controller = new MainWindow(log);
        MainWindow = controller;
        if (e.Args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
        {
            controller.ShowInTaskbar = true;
            controller.Show();
            log.Info("Diagnostics shown: visible=" + controller.IsVisible);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _privacyOnly?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static int StopRunningController()
    {
        var installedRoot = Path.GetFullPath(Path.Combine(AppPaths.Root, "app")) + Path.DirectorySeparatorChar;
        var exitCode = 0;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("ChatGPTController"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null || !Path.GetFullPath(path).StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    var emergency = Native.TopLevelWindows((uint)process.Id)
                        .FirstOrDefault(h => Native.WindowText(h) == "CGPTControllerEmergency");
                    // Reuse the tested emergency restore/exit handler. Never force-kill the controller.
                    if (emergency == IntPtr.Zero || !Native.PostMessage(emergency, 0x0312, new IntPtr(99), IntPtr.Zero) ||
                        !process.WaitForExit(5000)) exitCode = 2;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch { exitCode = 2; }
            }
        }
        return exitCode;
    }
}
