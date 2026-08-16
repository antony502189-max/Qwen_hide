using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ChatGPTDesktopController;

public sealed class WindowRecoverySnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public int ProcessId { get; set; }
    public long ProcessStartUtcTicks { get; set; }
    public long Hwnd { get; set; }
    public long OriginalExStyle { get; set; }
    public bool OriginalTopMost { get; set; }
    public bool OriginalVisible { get; set; }
    public bool OriginalLayered { get; set; }
    public byte OriginalAlpha { get; set; } = 255;
    public uint OriginalLayerFlags { get; set; } = Native.LWA_ALPHA;
    public uint OriginalColorKey { get; set; }
    public int PlacementFlags { get; set; }
    public int PlacementShowCmd { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool OriginalMinimized { get; set; }
    public bool OriginalMaximized { get; set; }
}

public sealed class RecoveryService
{
    private readonly AppLogger _log;
    private readonly string _journalPath;
    public RecoveryService(AppLogger log, string? journalPath = null) { _log = log; _journalPath = string.IsNullOrWhiteSpace(journalPath) ? AppPaths.RecoveryJournal : Path.GetFullPath(journalPath); }
    public string JournalPath => _journalPath;
    public bool HasPendingSnapshot => File.Exists(JournalPath);
    public bool Save(ChatGPTTarget target, IntPtr exStyle, bool topMost, bool visible, bool layered, byte alpha, uint flags, uint key)
    {
        if (!Native.TryPlacement(target.Hwnd, out var p)) return false;
        var snapshot = new WindowRecoverySnapshot
        {
            ProcessId = target.ProcessId, ProcessStartUtcTicks = target.ProcessStartUtcTicks, Hwnd = target.Hwnd.ToInt64(),
            OriginalExStyle = exStyle.ToInt64(), OriginalTopMost = topMost, OriginalVisible = visible, OriginalLayered = layered,
            OriginalAlpha = alpha, OriginalLayerFlags = flags, OriginalColorKey = key,
            PlacementFlags = p.Flags, PlacementShowCmd = p.ShowCmd, Left = p.RcNormalPosition.Left, Top = p.RcNormalPosition.Top, Right = p.RcNormalPosition.Right, Bottom = p.RcNormalPosition.Bottom,
            OriginalMinimized = Native.IsIconic(target.Hwnd), OriginalMaximized = Native.IsZoomed(target.Hwnd)
        };
        try
        {
            var parent = Path.GetDirectoryName(JournalPath); if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent); var tmp = JournalPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot)); File.Move(tmp, JournalPath, true);
            var verified = Read();
            return verified is not null && verified.Hwnd == snapshot.Hwnd && verified.ProcessStartUtcTicks == snapshot.ProcessStartUtcTicks && verified.OriginalExStyle == snapshot.OriginalExStyle;
        }
        catch (Exception ex) { _log.Error("Could not write recovery journal: " + ex.GetType().Name); return false; }
    }
    public bool TryRecoverStaleState()
    {
        if (!HasPendingSnapshot) return false;
        var s = Read(); if (s is null) { Delete(); return false; }
        var hwnd = new IntPtr(s.Hwnd);
        try
        {
            if (!Native.IsWindow(hwnd)) { Delete(); return false; }
            Native.GetWindowThreadProcessId(hwnd, out var owner); if (owner != s.ProcessId) { Delete(); return false; }
            using var p = Process.GetProcessById(s.ProcessId);
            if (s.ProcessStartUtcTicks != 0 && p.StartTime.ToUniversalTime().Ticks != s.ProcessStartUtcTicks) { Delete(); return false; }
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(s.OriginalExStyle));
            if (s.OriginalLayered) Native.SetLayeredWindowAttributes(hwnd, s.OriginalColorKey, s.OriginalAlpha, s.OriginalLayerFlags);
            Native.SetWindowPos(hwnd, s.OriginalTopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            var placement = new Native.WINDOWPLACEMENT { Length = Marshal.SizeOf<Native.WINDOWPLACEMENT>(), Flags = s.PlacementFlags, ShowCmd = s.PlacementShowCmd, RcNormalPosition = new Native.RECT { Left = s.Left, Top = s.Top, Right = s.Right, Bottom = s.Bottom } };
            Native.SetWindowPlacement(hwnd, ref placement);
            Native.ShowWindow(hwnd, !s.OriginalVisible ? Native.SW_HIDE : s.OriginalMinimized ? Native.SW_SHOWMINIMIZED : s.OriginalMaximized ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOW);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(s.OriginalExStyle));
            var complete = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() == s.OriginalExStyle && Native.IsWindowVisible(hwnd) == s.OriginalVisible;
            if (complete) { Delete(); _log.Info("Recovered stale ChatGPT Classic state."); }
            return complete;
        }
        catch (Exception ex) { _log.Error("Stale recovery deferred: " + ex.GetType().Name); return false; }
    }
    public void Clear() => Delete();
    private WindowRecoverySnapshot? Read() { try { return JsonSerializer.Deserialize<WindowRecoverySnapshot>(File.ReadAllText(JournalPath)); } catch { return null; } }
    private void Delete() { try { if (File.Exists(JournalPath)) File.Delete(JournalPath); } catch { } }
}
