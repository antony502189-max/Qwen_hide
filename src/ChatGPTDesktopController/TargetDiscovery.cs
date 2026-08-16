using Microsoft.Win32;
using System.Diagnostics;

namespace ChatGPTDesktopController;

public sealed record ChatGPTTarget(int ProcessId, IntPtr Hwnd, string ProcessName, string ExecutablePath, string WindowTitle, string WindowClass, long ProcessStartUtcTicks, string Architecture)
{
    public string Summary => $"{ProcessName} PID {ProcessId}, HWND 0x{Hwnd.ToInt64():X}";
}

public sealed class ChatGPTProcessLocator
{
    private readonly AppLogger _log;
    public ChatGPTProcessLocator(AppLogger log) => _log = log;

    public ChatGPTTarget? FindRunningTarget()
    {
        var candidates = new List<ChatGPTTarget>();
        foreach (var process in Process.GetProcessesByName("ChatGPT Classic"))
        {
            try
            {
                if (process.Id == Environment.ProcessId) continue;
                var path = TryPath(process);
                if (!IsChatGPTClassicExecutable(path)) continue;
                foreach (var hwnd in Native.TopLevelWindows((uint)process.Id))
                {
                    if (!Native.IsWindowVisible(hwnd)) continue;
                    if (Native.GetWindowRect(hwnd, out var r) && !Native.IsIconic(hwnd) && (r.Right-r.Left < 320 || r.Bottom-r.Top < 200)) continue;
                    candidates.Add(new(process.Id, hwnd, process.ProcessName, path!, Native.WindowText(hwnd), Native.WindowClass(hwnd), TryStart(process), DetectPortableExecutableArchitecture(path!)));
                }
            }
            catch { } finally { process.Dispose(); }
        }
        return candidates.OrderByDescending(Score).FirstOrDefault();
    }

    public string? FindInstalledExecutable(string? configured = null)
    {
        if (IsChatGPTClassicExecutable(configured)) return configured;
        var running = FindRunningTarget(); if (running is not null) return running.ExecutablePath;
        foreach (var keyName in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ChatGPT Classic.exe", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ChatGPT Classic.exe" })
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                try { var candidate = hive.OpenSubKey(keyName)?.GetValue(null)?.ToString()?.Trim('"'); if (IsChatGPTClassicExecutable(candidate)) return candidate; } catch { }
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try { return Directory.EnumerateFiles(windowsApps, "ChatGPT Classic.exe", SearchOption.AllDirectories).FirstOrDefault(IsChatGPTClassicExecutable); } catch { return null; }
    }
    public bool TryLaunch(string path)
    {
        if (!IsChatGPTClassicExecutable(path)) return false;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! }); return true; }
        catch (Exception ex) { _log.Error("Target launch failed: " + ex.GetType().Name); return false; }
    }
    public static bool IsChatGPTClassicExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !Path.GetFileName(path).Equals("ChatGPT Classic.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var full = Path.GetFullPath(path);
        return full.Contains("\\WindowsApps\\OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase);
    }
    public static string DetectPortableExecutableArchitecture(string path)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt16() != 0x5A4D) return "unknown";
            reader.BaseStream.Position = 0x3C; var offset = reader.ReadInt32();
            reader.BaseStream.Position = offset; if (reader.ReadUInt32() != 0x00004550) return "unknown";
            return reader.ReadUInt16() switch { 0x8664 => "x64", 0x014C => "x86", 0xAA64 => "ARM64", _ => "unknown" };
        }
        catch { return "unknown"; }
    }
    private static string? TryPath(Process p) { try { return p.MainModule?.FileName; } catch { return null; } }
    private static long TryStart(Process p) { try { return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
    private static int Score(ChatGPTTarget x) => (x.WindowTitle.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? 100 : 0) + (x.WindowClass.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? 20 : 0) + (Native.IsIconic(x.Hwnd) ? 0 : 10);
}
