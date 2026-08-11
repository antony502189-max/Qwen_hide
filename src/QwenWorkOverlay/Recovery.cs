using System.Diagnostics;
using System.Text.Json;

namespace QwenWorkOverlay;

public sealed class WindowRecoverySnapshot
{
    public int ProcessId { get; set; }
    public long ProcessStartUtcTicks { get; set; }
    public long Hwnd { get; set; }
    public long OriginalExStyle { get; set; }
    public bool OriginalTopMost { get; set; }
    public bool OriginalVisible { get; set; }
    public bool OriginalLayered { get; set; }
    public byte OriginalAlpha { get; set; } = 255;
    public uint OriginalLayerFlags { get; set; }
    public uint OriginalColorKey { get; set; }
}

public sealed class WindowRecoveryService
{
    private readonly AppLogger _log;
    private readonly string _path = Path.Combine(SettingsService.Root, "window-recovery.json");

    public WindowRecoveryService(AppLogger log) => _log = log;

    public void Save(QwenTarget target, IntPtr originalExStyle, bool originalTopMost, bool originalVisible,
        bool originalLayered, byte originalAlpha, uint originalLayerFlags, uint originalColorKey)
    {
        try
        {
            var snapshot = new WindowRecoverySnapshot
            {
                ProcessId = target.ProcessId,
                ProcessStartUtcTicks = target.ProcessStartUtcTicks,
                Hwnd = target.Hwnd.ToInt64(),
                OriginalExStyle = originalExStyle.ToInt64(),
                OriginalTopMost = originalTopMost,
                OriginalVisible = originalVisible,
                OriginalLayered = originalLayered,
                OriginalAlpha = originalAlpha,
                OriginalLayerFlags = originalLayerFlags,
                OriginalColorKey = originalColorKey
            };
            Directory.CreateDirectory(SettingsService.Root);
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.Error("Could not persist Qwen window recovery snapshot: " + ex.GetType().Name);
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (Exception ex) { _log.Error("Could not clear Qwen window recovery snapshot: " + ex.GetType().Name); }
    }

    public bool TryRecoverStaleState()
    {
        if (!File.Exists(_path)) return false;
        try
        {
            var snapshot = JsonSerializer.Deserialize<WindowRecoverySnapshot>(File.ReadAllText(_path));
            if (snapshot is null) { Clear(); return false; }

            var hwnd = new IntPtr(snapshot.Hwnd);
            if (!Native.IsWindow(hwnd)) { Clear(); return false; }
            Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid != snapshot.ProcessId) { Clear(); return false; }

            using var process = Process.GetProcessById(snapshot.ProcessId);
            var startTicks = process.StartTime.ToUniversalTime().Ticks;
            if (snapshot.ProcessStartUtcTicks != 0 && startTicks != snapshot.ProcessStartUtcTicks)
            {
                Clear();
                return false;
            }

            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(snapshot.OriginalExStyle));
            if (snapshot.OriginalLayered)
                Native.SetLayeredWindowAttributes(hwnd, snapshot.OriginalColorKey, snapshot.OriginalAlpha, snapshot.OriginalLayerFlags);

            Native.SetWindowPos(hwnd,
                snapshot.OriginalTopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

            Native.ShowWindowAsync(hwnd, snapshot.OriginalVisible ? Native.SW_SHOW : Native.SW_HIDE);
            _log.Info($"Recovered stale native Qwen window state from previous controller session (PID {snapshot.ProcessId}, HWND 0x{snapshot.Hwnd:X})");
            Clear();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Stale Qwen window recovery failed safely: " + ex.GetType().Name);
            Clear();
            return false;
        }
    }
}
