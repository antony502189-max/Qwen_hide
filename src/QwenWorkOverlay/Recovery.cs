using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace QwenWorkOverlay;

public sealed class WindowRecoverySnapshot
{
    // Missing from pre-v2 JSON must remain distinguishable from a v2 snapshot. A default of 2
    // would misinterpret legacy journals as containing zero-valued placement/parent metadata.
    public int RecoverySchemaVersion { get; set; }
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
    // These fields are captured and journaled before *any* SetParent/style mutation.  They are
    // deliberately plain values so an interrupted session can be restored before normal startup.
    public long OriginalParent { get; set; }
    public long OriginalStyle { get; set; }
    public int PlacementFlags { get; set; }
    public int PlacementShowCmd { get; set; }
    public int PlacementMinX { get; set; }
    public int PlacementMinY { get; set; }
    public int PlacementMaxX { get; set; }
    public int PlacementMaxY { get; set; }
    public int PlacementLeft { get; set; }
    public int PlacementTop { get; set; }
    public int PlacementRight { get; set; }
    public int PlacementBottom { get; set; }
    public bool OriginalMinimized { get; set; }
    public bool OriginalMaximized { get; set; }
    public uint OriginalDpi { get; set; }
    public long OriginalDpiAwarenessContext { get; set; }
    public bool PrivacyHostActive { get; set; }
    public long PrivacyHostHwnd { get; set; }
    public uint PrivacyHostDpi { get; set; }
}

public sealed class WindowRecoveryService
{
    private readonly AppLogger _log;
    private readonly string _path;

    public WindowRecoveryService(AppLogger log, string? journalPath = null)
    {
        _log = log;
        _path = string.IsNullOrWhiteSpace(journalPath)
            ? Path.Combine(SettingsService.Root, "window-recovery.json")
            : Path.GetFullPath(journalPath);
    }

    public string JournalPath => _path;
    public bool HasPendingSnapshot => File.Exists(_path);

    public bool Save(QwenTarget target, IntPtr originalExStyle, bool originalTopMost, bool originalVisible,
        bool originalLayered, byte originalAlpha, uint originalLayerFlags, uint originalColorKey)
    {
        try
        {
            if (!Native.TryGetWindowPlacement(target.Hwnd, out var placement))
            {
                _log.Error("Could not capture complete Qwen WINDOWPLACEMENT; refusing unsafe window mutation; win32=" + Marshal.GetLastWin32Error());
                return false;
            }
            var originalStyle = Native.GetWindowLongPtr(target.Hwnd, Native.GWL_STYLE).ToInt64();
            var originalParent = Native.GetParent(target.Hwnd).ToInt64();
            var dpi = Native.GetDpiForWindow(target.Hwnd);
            var dpiAwarenessContext = Native.GetWindowDpiAwarenessContext(target.Hwnd).ToInt64();
            var snapshot = new WindowRecoverySnapshot
            {
                RecoverySchemaVersion = 2,
                ProcessId = target.ProcessId,
                ProcessStartUtcTicks = target.ProcessStartUtcTicks,
                Hwnd = target.Hwnd.ToInt64(),
                OriginalExStyle = originalExStyle.ToInt64(),
                OriginalTopMost = originalTopMost,
                OriginalVisible = originalVisible,
                OriginalLayered = originalLayered,
                OriginalAlpha = originalAlpha,
                OriginalLayerFlags = originalLayerFlags,
                OriginalColorKey = originalColorKey,
                OriginalParent = originalParent,
                OriginalStyle = originalStyle,
                PlacementFlags = placement.Flags,
                PlacementShowCmd = placement.ShowCmd,
                PlacementMinX = placement.PtMinPosition.X,
                PlacementMinY = placement.PtMinPosition.Y,
                PlacementMaxX = placement.PtMaxPosition.X,
                PlacementMaxY = placement.PtMaxPosition.Y,
                PlacementLeft = placement.RcNormalPosition.Left,
                PlacementTop = placement.RcNormalPosition.Top,
                PlacementRight = placement.RcNormalPosition.Right,
                PlacementBottom = placement.RcNormalPosition.Bottom,
                OriginalMinimized = Native.IsIconic(target.Hwnd),
                OriginalMaximized = Native.IsZoomed(target.Hwnd),
                OriginalDpi = dpi,
                OriginalDpiAwarenessContext = dpiAwarenessContext
            };
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);

            // Read the file back before mutating the external Qwen HWND. If persistence is unavailable or
            // corrupt, a hard crash could otherwise leave Qwen permanently click-through/transparent.
            var verify = TryReadSnapshot();
            var ok = verify is not null &&
                     verify.ProcessId == snapshot.ProcessId &&
                     verify.ProcessStartUtcTicks == snapshot.ProcessStartUtcTicks &&
                     verify.Hwnd == snapshot.Hwnd &&
                     verify.OriginalExStyle == snapshot.OriginalExStyle &&
                     verify.OriginalTopMost == snapshot.OriginalTopMost &&
                     verify.OriginalVisible == snapshot.OriginalVisible &&
                     verify.OriginalLayered == snapshot.OriginalLayered &&
                     verify.OriginalAlpha == snapshot.OriginalAlpha &&
                     verify.OriginalLayerFlags == snapshot.OriginalLayerFlags &&
                     verify.OriginalColorKey == snapshot.OriginalColorKey &&
                     verify.RecoverySchemaVersion >= 2 &&
                     verify.OriginalParent == snapshot.OriginalParent &&
                     verify.OriginalStyle == snapshot.OriginalStyle &&
                     verify.PlacementShowCmd == snapshot.PlacementShowCmd &&
                     verify.PlacementLeft == snapshot.PlacementLeft &&
                     verify.PlacementTop == snapshot.PlacementTop &&
                     verify.PlacementRight == snapshot.PlacementRight &&
                     verify.PlacementBottom == snapshot.PlacementBottom &&
                     verify.OriginalMinimized == snapshot.OriginalMinimized &&
                     verify.OriginalMaximized == snapshot.OriginalMaximized &&
                     verify.OriginalDpi == snapshot.OriginalDpi &&
                     verify.OriginalDpiAwarenessContext == snapshot.OriginalDpiAwarenessContext;
            if (!ok)
            {
                _log.Error("Qwen recovery journal verification failed; refusing unsafe native window mutation");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Could not persist Qwen window recovery snapshot: " + ex.GetType().Name);
            return false;
        }
    }

    public void Clear()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var snapshot = TryReadSnapshot();
            if (snapshot is null)
            {
                DeleteJournal();
                return;
            }

            var hwnd = new IntPtr(snapshot.Hwnd);
            if (!Native.IsWindow(hwnd))
            {
                DeleteJournal();
                return;
            }

            Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid != snapshot.ProcessId)
            {
                DeleteJournal();
                return;
            }

            var currentStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            var styleMatches = snapshot.RecoverySchemaVersion < 2
                ? LegacyRecoveryPolicy.IsSafe(currentStyle, Native.IsWindowVisible(hwnd) == snapshot.OriginalVisible)
                : currentStyle == snapshot.OriginalExStyle;
            var parentMatches = snapshot.RecoverySchemaVersion < 2 || Native.GetParent(hwnd).ToInt64() == snapshot.OriginalParent;
            // GWL_STYLE is only controller-mutated by the cross-process privacy host. Other
            // native mutations intentionally leave it alone, and window managers may normalize
            // unrelated style bits while applying an ex-style frame transition.
            var baseStyleMatches = snapshot.RecoverySchemaVersion < 2 || !snapshot.PrivacyHostActive || Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() == snapshot.OriginalStyle;
            var topMostMatches = ((currentStyle & Native.WS_EX_TOPMOST) != 0) == snapshot.OriginalTopMost;
            var visibleMatches = Native.IsWindowVisible(hwnd) == snapshot.OriginalVisible;
            var minimizedMatches = snapshot.RecoverySchemaVersion < 2 || Native.IsIconic(hwnd) == snapshot.OriginalMinimized;
            var maximizedMatches = snapshot.RecoverySchemaVersion < 2 || Native.IsZoomed(hwnd) == snapshot.OriginalMaximized;
            var placementMatches = snapshot.RecoverySchemaVersion < 2 || MatchesOriginalPlacement(hwnd, snapshot);
            var layeredMatches = true;
            if (snapshot.OriginalLayered && Native.GetLayeredWindowAttributes(hwnd, out var key, out var alpha, out var flags))
                layeredMatches = key == snapshot.OriginalColorKey && alpha == snapshot.OriginalAlpha && flags == snapshot.OriginalLayerFlags;

            if (styleMatches && parentMatches && baseStyleMatches && topMostMatches && visibleMatches && minimizedMatches && maximizedMatches && placementMatches && layeredMatches)
                DeleteJournal();
            else
                _log.Error($"Keeping Qwen recovery journal because restoration verification failed: parent={parentMatches}, style={styleMatches}, baseStyle={baseStyleMatches}, topmost={topMostMatches}, visible={visibleMatches}, minimized={minimizedMatches}, maximized={maximizedMatches}, placement={placementMatches}, layered={layeredMatches}");
        }
        catch (Exception ex)
        {
            _log.Error("Could not verify/clear Qwen window recovery snapshot: " + ex.GetType().Name);
        }
    }

    public bool MarkPrivacyHost(IntPtr hostHwnd, uint hostDpi)
    {
        try
        {
            var snapshot = TryReadSnapshot();
            if (snapshot is null || snapshot.RecoverySchemaVersion < 2) return false;
            snapshot.PrivacyHostActive = true;
            snapshot.PrivacyHostHwnd = hostHwnd.ToInt64();
            snapshot.PrivacyHostDpi = hostDpi;
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
            var verified = TryReadSnapshot();
            return verified is not null && verified.PrivacyHostActive && verified.PrivacyHostHwnd == snapshot.PrivacyHostHwnd && verified.PrivacyHostDpi == hostDpi;
        }
        catch (Exception ex)
        {
            _log.Error("Could not persist privacy-host recovery state: " + ex.GetType().Name);
            return false;
        }
    }

    public bool TryRecoverStaleState()
    {
        if (!File.Exists(_path)) return false;
        try
        {
            var snapshot = TryReadSnapshot();
            if (snapshot is null)
            {
                DeleteJournal();
                return false;
            }

            var hwnd = new IntPtr(snapshot.Hwnd);
            if (!Native.IsWindow(hwnd))
            {
                DeleteJournal();
                return false;
            }

            Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid != snapshot.ProcessId)
            {
                DeleteJournal();
                return false;
            }

            using var process = Process.GetProcessById(snapshot.ProcessId);
            var startTicks = process.StartTime.ToUniversalTime().Ticks;
            if (snapshot.ProcessStartUtcTicks != 0 && startTicks != snapshot.ProcessStartUtcTicks)
            {
                DeleteJournal();
                return false;
            }

            var parentOk = true;
            var baseStyleOk = true;
            var placementOk = true;
            if (snapshot.RecoverySchemaVersion >= 2)
            {
                if (Native.GetParent(hwnd).ToInt64() != snapshot.OriginalParent)
                {
                    Marshal.SetLastPInvokeError(0);
                    Native.SetParent(hwnd, new IntPtr(snapshot.OriginalParent));
                }

                Marshal.SetLastPInvokeError(0);
                Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(snapshot.OriginalStyle));
                // SetWindowLongPtr may legitimately return zero; the post-condition is the only
                // useful success criterion and avoids retaining a recovery journal on stale last-error.
                baseStyleOk = !snapshot.PrivacyHostActive || Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() == snapshot.OriginalStyle;
            }

            Marshal.SetLastPInvokeError(0);
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(snapshot.OriginalExStyle));
            var styleOk = true;

            var layeredOk = true;
            if (snapshot.OriginalLayered)
                layeredOk = Native.SetLayeredWindowAttributes(hwnd, snapshot.OriginalColorKey, snapshot.OriginalAlpha, snapshot.OriginalLayerFlags);

            var topMostOk = Native.SetWindowPos(hwnd,
                snapshot.OriginalTopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

            if (snapshot.RecoverySchemaVersion >= 2)
            {
                var placement = new Native.WINDOWPLACEMENT
                {
                    Length = Marshal.SizeOf<Native.WINDOWPLACEMENT>(),
                    Flags = snapshot.PlacementFlags,
                    ShowCmd = snapshot.PlacementShowCmd,
                    PtMinPosition = new Native.POINT { X = snapshot.PlacementMinX, Y = snapshot.PlacementMinY },
                    PtMaxPosition = new Native.POINT { X = snapshot.PlacementMaxX, Y = snapshot.PlacementMaxY },
                    RcNormalPosition = new Native.RECT
                    {
                        Left = snapshot.PlacementLeft, Top = snapshot.PlacementTop,
                        Right = snapshot.PlacementRight, Bottom = snapshot.PlacementBottom
                    }
                };
                placementOk = Native.SetWindowPlacement(hwnd, ref placement);
            }

            // ShowWindow after SetWindowPlacement must preserve the recorded visual state. A generic
            // SW_SHOW would silently turn a minimized/maximized Qwen into a normal window.
            var showCommand = !snapshot.OriginalVisible
                ? Native.SW_HIDE
                : snapshot.OriginalMinimized
                    ? Native.SW_SHOWMINIMIZED
                    : snapshot.OriginalMaximized
                        ? Native.SW_SHOWMAXIMIZED
                        : Native.SW_SHOW;
            Native.ShowWindow(hwnd, showCommand);

            // SetWindowPos topmost transitions can rewrite WS_EX_TOPMOST. Restore the exact
            // recorded extended-style word after the Z-order/placement calls, then verify it.
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(snapshot.OriginalExStyle));
            var restoredExStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            styleOk = snapshot.RecoverySchemaVersion < 2
                ? LegacyRecoveryPolicy.IsSafe(restoredExStyle, Native.IsWindowVisible(hwnd) == snapshot.OriginalVisible)
                : restoredExStyle == snapshot.OriginalExStyle;

            // GetParent's interpretation depends on WS_CHILD. Check only after both style words have
            // been restored; checking immediately after SetParent(..., NULL) falsely reports desktop
            // ownership while the old child style is still present.
            if (snapshot.RecoverySchemaVersion >= 2)
                parentOk = Native.GetParent(hwnd).ToInt64() == snapshot.OriginalParent;

            var visualStateOk = snapshot.RecoverySchemaVersion < 2 ||
                (Native.IsWindowVisible(hwnd) == snapshot.OriginalVisible &&
                 Native.IsIconic(hwnd) == snapshot.OriginalMinimized &&
                 Native.IsZoomed(hwnd) == snapshot.OriginalMaximized &&
                 MatchesOriginalPlacement(hwnd, snapshot));

            if (!parentOk || !baseStyleOk || !styleOk || !layeredOk || !topMostOk || !placementOk || !visualStateOk)
            {
                _log.Error($"Qwen window recovery incomplete; keeping journal for retry. parent={parentOk}, baseStyle={baseStyleOk}, style={styleOk}, layered={layeredOk}, topmost={topMostOk}, placement={placementOk}, visualState={visualStateOk}");
                return false;
            }

            _log.Info($"Recovered native Qwen window state (PID {snapshot.ProcessId}, HWND 0x{snapshot.Hwnd:X})");
            Clear();
            return !HasPendingSnapshot;
        }
        catch (ArgumentException)
        {
            DeleteJournal();
            return false;
        }
        catch (InvalidOperationException)
        {
            DeleteJournal();
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Qwen window recovery failed safely and will be retried: " + ex.GetType().Name);
            return false;
        }
    }

    private WindowRecoverySnapshot? TryReadSnapshot()
    {
        try { return JsonSerializer.Deserialize<WindowRecoverySnapshot>(File.ReadAllText(_path)); }
        catch { return null; }
    }

    private static bool MatchesOriginalPlacement(IntPtr hwnd, WindowRecoverySnapshot snapshot)
    {
        if (!Native.TryGetWindowPlacement(hwnd, out var current)) return false;
        return current.RcNormalPosition.Left == snapshot.PlacementLeft &&
               current.RcNormalPosition.Top == snapshot.PlacementTop &&
               current.RcNormalPosition.Right == snapshot.PlacementRight &&
               current.RcNormalPosition.Bottom == snapshot.PlacementBottom;
    }

    private void DeleteJournal()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (Exception ex) { _log.Error("Could not delete Qwen recovery journal: " + ex.GetType().Name); }
    }
}
