using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QwenWorkOverlay;

public sealed record QwenTarget(int ProcessId, IntPtr Hwnd, string ProcessName, string? ExecutablePath, string WindowTitle, string WindowClass)
{
    public string Summary => $"{ProcessName} (PID {ProcessId}, HWND 0x{Hwnd.ToInt64():X})";
}

public sealed class QwenProcessLocator
{
    private readonly AppLogger _log;
    public QwenProcessLocator(AppLogger log) => _log = log;

    public QwenTarget? FindRunningTarget()
    {
        var currentPid = Environment.ProcessId;
        var candidates = new List<QwenTarget>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentPid) continue;
                var processName = process.ProcessName;
                var nameLooksRight = processName.Contains("qwen", StringComparison.OrdinalIgnoreCase);
                if (!nameLooksRight) continue;

                foreach (var hwnd in Native.EnumerateTopLevelWindows((uint)process.Id))
                {
                    if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) continue;
                    if (!Native.GetWindowRect(hwnd, out var rect)) continue;
                    if (rect.Right - rect.Left < 320 || rect.Bottom - rect.Top < 200) continue;
                    var title = Native.GetWindowText(hwnd);
                    var windowClass = Native.GetWindowClass(hwnd);
                    candidates.Add(new QwenTarget(process.Id, hwnd, processName, TryGetExecutablePath(process), title, windowClass));
                }
            }
            catch
            {
                // Processes may exit while being enumerated.
            }
            finally
            {
                process.Dispose();
            }
        }

        return candidates
            .OrderByDescending(Score)
            .ThenByDescending(x => Native.GetWindowArea(x.Hwnd))
            .FirstOrDefault();
    }

    public string? FindInstalledExecutable(string? configuredPath)
    {
        if (IsExecutable(configuredPath)) return configuredPath;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Qwen.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Qwen.exe"
            })
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath);
                    var value = key?.GetValue(null)?.ToString()?.Trim('"');
                    if (IsExecutable(value)) return value;
                }
                catch { }
            }
        }

        var directCandidates = new List<string>();
        AddCandidate(directCandidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Qwen", "Qwen.exe");
        AddCandidate(directCandidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qwen", "Qwen.exe");
        AddCandidate(directCandidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Qwen", "Qwen.exe");
        AddCandidate(directCandidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Qwen", "Qwen.exe");
        foreach (var candidate in directCandidates)
            if (IsExecutable(candidate)) return candidate;

        var searchRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "*Qwen*", SearchOption.TopDirectoryOnly))
                {
                    var direct = Path.Combine(directory, "Qwen.exe");
                    if (IsExecutable(direct)) return direct;
                    var nested = Directory.EnumerateFiles(directory, "Qwen.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (IsExecutable(nested)) return nested;
                }
            }
            catch { }
        }

        return null;
    }

    public bool TryLaunch(string executablePath)
    {
        if (!IsExecutable(executablePath)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executablePath)! });
            _log.Info("Launched installed Qwen desktop: " + executablePath);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to launch installed Qwen desktop: " + ex.GetType().Name);
            return false;
        }
    }

    private static int Score(QwenTarget target)
    {
        var score = 0;
        if (target.ProcessName.Equals("Qwen", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (target.WindowTitle.Contains("Qwen", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (Path.GetFileName(target.ExecutablePath).Equals("Qwen.exe", StringComparison.OrdinalIgnoreCase)) score += 25;
        return score;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static bool IsExecutable(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
    private static void AddCandidate(List<string> list, params string[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace)) return;
        list.Add(Path.Combine(parts));
    }
}

public sealed class QwenWindowController : IDisposable
{
    private readonly AppLogger _log;
    private QwenTarget? _target;
    private nint _originalExStyle;
    private bool _originalTopMost;
    private bool _originalVisible;
    private bool _originalLayered;
    private byte _originalAlpha = 255;
    private uint _originalLayerFlags;
    private uint _originalColorKey;
    private bool _clickThrough;
    private double _opacity = 1;
    private bool _topMost;
    private bool _hidden;

    public QwenWindowController(AppLogger log) => _log = log;
    public QwenTarget? Target => _target;
    public bool IsAttached => _target is not null && Native.IsWindow(_target.Hwnd);
    public bool ClickThrough => _clickThrough;
    public double Opacity => _opacity;
    public bool TopMost => _topMost;
    public bool Hidden => _hidden;

    public bool Attach(QwenTarget target, double opacity, bool topMost)
    {
        if (IsAttached && _target!.Hwnd == target.Hwnd) return true;
        Detach(restore: true);
        if (!Native.IsWindow(target.Hwnd)) return false;

        _target = target;
        _originalExStyle = Native.GetWindowLongPtr(target.Hwnd, Native.GWL_EXSTYLE);
        _originalTopMost = (_originalExStyle.ToInt64() & Native.WS_EX_TOPMOST) != 0;
        _originalVisible = Native.IsWindowVisible(target.Hwnd);
        _originalLayered = (_originalExStyle.ToInt64() & Native.WS_EX_LAYERED) != 0;
        if (_originalLayered && Native.GetLayeredWindowAttributes(target.Hwnd, out var key, out var alpha, out var flags))
        {
            _originalColorKey = key;
            _originalAlpha = alpha;
            _originalLayerFlags = flags;
        }

        _hidden = !_originalVisible;
        SetOpacity(opacity);
        SetTopMost(topMost);
        _log.Info("Attached to native Qwen desktop: " + target.Summary);
        return true;
    }

    public bool SetOpacity(double value)
    {
        if (!IsAttached) return false;
        value = Math.Clamp(value, .35, 1.0);
        _opacity = value;
        return ApplyStyles();
    }

    public bool SetClickThrough(bool enabled)
    {
        if (!IsAttached) return false;
        _clickThrough = enabled;
        return ApplyStyles();
    }

    public bool SetTopMost(bool enabled)
    {
        if (!IsAttached) return false;
        _topMost = enabled;
        return Native.SetWindowPos(
            _target!.Hwnd,
            enabled ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
            0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public bool ToggleVisibility()
    {
        if (!IsAttached) return false;
        _hidden = !_hidden;
        return Native.ShowWindowAsync(_target!.Hwnd, _hidden ? Native.SW_HIDE : Native.SW_SHOW);
    }

    public bool ShowAndActivate()
    {
        if (!IsAttached) return false;
        _hidden = false;
        Native.ShowWindowAsync(_target!.Hwnd, Native.SW_RESTORE);
        Native.ShowWindowAsync(_target.Hwnd, Native.SW_SHOW);
        return Native.SetForegroundWindow(_target.Hwnd);
    }

    private bool ApplyStyles()
    {
        if (!IsAttached) return false;
        var hwnd = _target!.Hwnd;
        var desired = _originalExStyle.ToInt64();
        if (_opacity < .999) desired |= Native.WS_EX_LAYERED;
        if (_clickThrough) desired |= Native.WS_EX_TRANSPARENT;
        else if ((_originalExStyle.ToInt64() & Native.WS_EX_TRANSPARENT) == 0) desired &= ~Native.WS_EX_TRANSPARENT;

        var written = Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(desired));
        if (written == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            _log.Error("SetWindowLongPtr failed for native Qwen HWND");
            return false;
        }

        if (_opacity < .999)
        {
            if (!Native.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(_opacity * 255), Native.LWA_ALPHA))
            {
                _log.Error("SetLayeredWindowAttributes failed for native Qwen HWND");
                return false;
            }
        }
        else if (_originalLayered)
        {
            Native.SetLayeredWindowAttributes(hwnd, _originalColorKey, _originalAlpha, _originalLayerFlags);
        }
        else
        {
            var withoutLayer = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & ~Native.WS_EX_LAYERED;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(withoutLayer));
        }

        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        return true;
    }

    public void Detach(bool restore)
    {
        var target = _target;
        _target = null;
        if (target is null || !Native.IsWindow(target.Hwnd)) return;
        if (restore)
        {
            Native.SetWindowLongPtr(target.Hwnd, Native.GWL_EXSTYLE, _originalExStyle);
            if (_originalLayered)
                Native.SetLayeredWindowAttributes(target.Hwnd, _originalColorKey, _originalAlpha, _originalLayerFlags);
            Native.SetWindowPos(target.Hwnd, _originalTopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            if (_originalVisible) Native.ShowWindowAsync(target.Hwnd, Native.SW_SHOW);
            else Native.ShowWindowAsync(target.Hwnd, Native.SW_HIDE);
        }
        _clickThrough = false;
        _hidden = false;
        _log.Info("Detached from native Qwen desktop");
    }

    public void Dispose() => Detach(restore: true);
}

public sealed class ForegroundWindowTracker : IDisposable
{
    private readonly Func<IntPtr?> _qwenHandle;
    private readonly System.Threading.Timer _timer;
    private IntPtr _lastNonQwen;

    public ForegroundWindowTracker(Func<IntPtr?> qwenHandle)
    {
        _qwenHandle = qwenHandle;
        _timer = new System.Threading.Timer(_ => Sample(), null, 0, 250);
    }

    public IntPtr LastNonQwenWindow => Native.IsWindow(_lastNonQwen) ? _lastNonQwen : IntPtr.Zero;

    private void Sample()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !Native.IsWindow(foreground)) return;
        var root = Native.GetAncestor(foreground, Native.GA_ROOT);
        var qwen = _qwenHandle();
        if (qwen.HasValue && root == qwen.Value) return;
        Native.GetWindowThreadProcessId(root, out var pid);
        if (pid == Environment.ProcessId) return;
        _lastNonQwen = root;
    }

    public void Dispose() => _timer.Dispose();
}
