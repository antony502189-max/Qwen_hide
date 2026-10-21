using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace ChatGPTDesktopController;

internal static class CurrentAppPrivacyPolicy
{
    // Match the Store package observed on this computer; version-independent, publisher-specific.
    // Never select an unrelated ChatGPT.exe by its filename alone.
    internal static bool IsPackagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
            return Regex.IsMatch(relative,
                @"^OpenAI\.Codex_[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+_x64__2p2nqsd0c76g0\\app\\ChatGPT\.exe$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch { return false; }
    }

    internal static bool IsInstalledTarget(string? path) => IsPackagePath(path) && File.Exists(path);
}

internal sealed class PrivacyOnlyController : IDisposable
{
    private readonly PrivacyGuardService _guard;
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private readonly DispatcherTimer _statusTimer;
    private readonly System.Windows.Forms.ToolStripMenuItem _status;
    private readonly AppLogger _log;
    private PrivacyGuardState? _lastState;

    public PrivacyOnlyController(AppLogger log)
    {
        _log = log;
        _guard = new PrivacyGuardService(log, currentAppOnly: true);
        _menu = new System.Windows.Forms.ContextMenuStrip();
        _status = new System.Windows.Forms.ToolStripMenuItem("Проверка защиты ChatGPT…") { Enabled = false };
        _menu.Items.Add(_status);
        _menu.Items.Add("Проверить повторно", null, (_, _) => _guard.ScanNow());
        _menu.Items.Add("Открыть журнал", null, (_, _) => Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true }));
        _menu.Items.Add("Завершить контроллер", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "ChatGPT: проверка защиты от захвата",
            Icon = SystemIcons.Shield,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        log.Info("Current ChatGPT/Codex privacy-only controller started; all matching top-level windows monitored; no hotkeys, audio, opacity, or visibility changes.");
    }

    private void UpdateStatus()
    {
        var snapshot = _guard.Snapshot;
        var summary = snapshot.State switch
        {
            PrivacyGuardState.Protected => $"ChatGPT: защита активна ({snapshot.WindowsVerified} окон)",
            PrivacyGuardState.Waiting => "ChatGPT: ожидание запуска приложения",
            PrivacyGuardState.Partial => "ChatGPT: защита подтверждена не для всех окон",
            _ => "ChatGPT: ошибка защиты — проверьте журнал"
        };
        _icon.Text = summary.Length > 63 ? summary[..63] : summary;
        _icon.Icon = snapshot.State == PrivacyGuardState.Protected ? SystemIcons.Shield : SystemIcons.Warning;
        _status.Text = summary;
        if (_lastState == snapshot.State) return;
        _lastState = snapshot.State;
        _log.Info("Current app privacy state: " + snapshot.State + "; " + snapshot.Detail);
    }

    public void Dispose()
    {
        _statusTimer.Stop();
        _guard.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _log.Info("Current app privacy controller stopped; existing window affinity preserved.");
    }
}
