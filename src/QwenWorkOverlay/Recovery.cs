using System.Diagnostics;
using System.Runtime.InteropServices;
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
    public bool HasPendingSnapshot => File.Exists(_path);

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
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
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
            if (snapshot is null)
            {
                Clear();
                return false;
            }

            var hwnd = new IntPtr(snapshot.Hwnd);
            if (!Native.IsWindow(hwnd))
            {
                // The original window no longer exists, so there is nothing left to recover.
                Clear();
                return false;
            }

            Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid != snapshot.ProcessId)
            {
                // HWND was reused by another process: never touch it.
                Clear();
                return false;
            }

            using var process = Process.GetProcessById(snapshot.ProcessId);
            var startTicks = process.StartTime.ToUniversalTime().Ticks;
            if (snapshot.ProcessStartUtcTicks != 0 && startTicks != snapshot.ProcessStartUtcTicks)
            {
                // PID was reused: never apply stale styles to the new process.
                Clear();
                return false;
            }

            Marshal.SetLastPInvokeError(0);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(snapshot.OriginalExStyle));
            var styleOk = Marshal.GetLastWin32Error() == 0;

            var layeredOk = true;
            if (snapshot.OriginalLayered)
                layeredOk = Native.SetLayeredWindowAttributes(hwnd, snapshot.OriginalColorKey, snapshot.OriginalAlpha, snapshot.OriginalLayerFlags);

            var topMostOk = Native.SetWindowPos(hwnd,
                snapshot.OriginalTopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

            // Visibility restoration is best-effort: ShowWindowAsync's return value reflects prior state, not success.
            Native.ShowWindowAsync(hwnd, snapshot.OriginalVisible ? Native.SW_SHOW : Native.SW_HIDE);

            if (!styleOk || !layeredOk || !topMostOk)
            {
                _log.Error($"Qwen window recovery incomplete; keeping journal for retry. style={styleOk}, layered={layeredOk}, topmost={topMostOk}");
                return false;
            }

            _log.Info($"Recovered native Qwen window state (PID {snapshot.ProcessId}, HWND 0x{snapshot.Hwnd:X})");
            Clear();
            return true;
        }
        catch (ArgumentException)
        {
            // Process exited between validation steps.
            Clear();
            return false;
        }
        catch (InvalidOperationException)
        {
            Clear();
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Qwen window recovery failed safely and will be retried: " + ex.GetType().Name);
            return false;
        }
    }
}
