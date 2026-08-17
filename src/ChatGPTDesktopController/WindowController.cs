namespace ChatGPTDesktopController;

public sealed class WindowController : IDisposable
{
    private readonly AppLogger _log;
    private readonly RecoveryService _recovery;
    private readonly object _stateGate = new();
    private ChatGPTTarget? _target;
    private IntPtr _originalExStyle;
    private bool _originalTopMost, _originalVisible, _originalLayered, _journaled, _hiddenFromMinimized, _hiddenFromMaximized;
    private byte _originalAlpha = 255;
    private uint _originalFlags = Native.LWA_ALPHA, _originalColorKey;
    private int _interactionEpoch;

    public ChatGPTTarget? Target => _target;
    public bool IsAttached => _target is { } t && Native.IsWindow(t.Hwnd);
    public bool ClickThrough { get; private set; }
    public bool TopMost { get; private set; }
    public bool Hidden { get; private set; }
    public double Opacity { get; private set; } = 1;

    public WindowController(AppLogger log, RecoveryService recovery)
    {
        _log = log;
        _recovery = recovery;
    }

    public bool Attach(ChatGPTTarget target)
    {
        lock (_stateGate)
        {
            if (IsAttached && _target!.Hwnd == target.Hwnd) return true;
            RestoreLocked();
            _target = null;
            _journaled = false;
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
                _originalFlags = flags;
            }

            ResetLogicalToOriginal();
            _log.Info("Attached " + target.Summary);
            return true;
        }
    }

    public bool ToggleVisibility() => Mutate(() =>
    {
        if (!Hidden)
        {
            _hiddenFromMinimized = Native.IsIconic(_target!.Hwnd);
            _hiddenFromMaximized = Native.IsZoomed(_target.Hwnd);
            Native.ShowWindow(_target.Hwnd, Native.SW_HIDE);
            if (Native.IsWindowVisible(_target.Hwnd)) return false;
            Hidden = true;
            return true;
        }

        Native.ShowWindow(_target!.Hwnd, VisibilityRestorePolicy.Command(_hiddenFromMinimized, _hiddenFromMaximized));
        if (!Native.IsWindowVisible(_target.Hwnd)) return false;
        Hidden = false;
        return true;
    });

    public bool ToggleClickThrough() => Mutate(() =>
    {
        var requested = !ClickThrough;
        if (!ApplyVisuals(requested, Opacity)) return false;
        ClickThrough = requested;
        return true;
    });

    public bool ToggleTopMost() => Mutate(() =>
    {
        var requested = !TopMost;
        var hwnd = _target!.Hwnd;
        var flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_FRAMECHANGED;
        if (!requested) flags |= Native.SWP_NOACTIVATE;

        if (!Native.SetWindowPos(hwnd, requested ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags))
            return false;
        if (Native.IsTopMost(hwnd) != requested) return false;

        TopMost = requested;
        if (requested && !Native.TryActivateWindow(hwnd))
            _log.Error("TopMost enabled but foreground activation was rejected by Windows");

        // Foreground activation of Electron/Chromium can rebuild the native surface and
        // normalize layered-window attributes. Re-apply the CURRENT controller visuals
        // after activation so toggling TopMost never changes opacity or click-through.
        if (!ApplyVisuals(ClickThrough, Opacity)) return false;
        return true;
    });

    public bool AdjustOpacity(double delta) => SetOpacity(Opacity + delta);

    public bool SetOpacity(double value) => Mutate(() =>
    {
        var requested = OpacityPolicy.Clamp(value);
        if (!ApplyVisuals(ClickThrough, requested)) return false;
        Opacity = requested;
        return true;
    });

    public bool EnsureInteractive(Action operation)
    {
        bool prior;
        int epoch;
        lock (_stateGate)
        {
            if (!IsAttached) return false;
            prior = ClickThrough;
            if (prior)
            {
                if (!EnsureJournal()) return false;
                if (!ApplyVisuals(false, Opacity))
                {
                    RecoverAfterFailedMutation("temporary interactive transition");
                    return false;
                }
                ClickThrough = false;
            }
            epoch = _interactionEpoch;
        }

        try
        {
            operation();
            return true;
        }
        finally
        {
            lock (_stateGate)
            {
                if (prior && epoch == _interactionEpoch && IsAttached)
                {
                    if (!ApplyVisuals(true, Opacity))
                    {
                        _log.Error("Failed to restore click-through after temporary interaction");
                        RecoverAfterFailedMutation("temporary interaction restore");
                    }
                    else ClickThrough = true;
                }
            }
        }
    }

    public bool Restore()
    {
        lock (_stateGate) return RestoreLocked();
    }

    private bool RestoreLocked()
    {
        _interactionEpoch++;
        if (!_journaled) return true;

        var recovered = _recovery.TryRecoverStaleState();
        var journalGone = !_recovery.HasPendingSnapshot;
        if (!recovered && !journalGone) return false;

        _journaled = false;
        ResetLogicalToOriginal();
        return true;
    }

    private bool Mutate(Func<bool> action)
    {
        lock (_stateGate)
        {
            if (!IsAttached) return false;
            if (!EnsureJournal()) return false;
            try
            {
                var ok = action();
                if (!ok) RecoverAfterFailedMutation("window mutation verification");
                return ok;
            }
            catch (Exception ex)
            {
                _log.Error("Window mutation failed: " + ex.GetType().Name);
                RecoverAfterFailedMutation("window mutation exception");
                return false;
            }
        }
    }

    private void RecoverAfterFailedMutation(string context)
    {
        _log.Error(context + " failed; restoring original target state");
        var recovered = _recovery.TryRecoverStaleState();
        var journalGone = !_recovery.HasPendingSnapshot;
        _journaled = !journalGone;
        if (recovered || journalGone) ResetLogicalToOriginal();
        else SyncLogicalFromNative();
    }

    private bool ApplyVisuals(bool clickThrough, double opacity)
    {
        if (!IsAttached) return false;
        var hwnd = _target!.Hwnd;

        // Preserve the target's CURRENT extended style. In particular, TopMost can be
        // toggled independently of opacity/click-through and must not be reset back to
        // the style captured when the controller first attached.
        var currentStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        var style = WindowStylePolicy.ComposeVisualStyle(currentStyle, clickThrough);
        var alpha = (byte)Math.Round(OpacityPolicy.Clamp(opacity) * 255);

        if (!Native.TrySetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style))) return false;
        if (!Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA)) return false;

        // Re-assert the actual Z-order band after every visual mutation. Chromium/Electron
        // windows can otherwise keep the WS_EX_TOPMOST bit while drifting out of the TopMost band.
        var insertAfter = TopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST;
        if (!Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED))
            return false;

        var actualStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        var hasLayered = (actualStyle & Native.WS_EX_LAYERED) != 0;
        var hasTransparent = (actualStyle & Native.WS_EX_TRANSPARENT) != 0;
        var hasTopMost = (actualStyle & Native.WS_EX_TOPMOST) != 0;
        if (!hasLayered || hasTransparent != clickThrough || hasTopMost != TopMost) return false;

        if (!Native.GetLayeredWindowAttributes(hwnd, out _, out var actualAlpha, out var actualFlags)) return false;
        return (actualFlags & Native.LWA_ALPHA) != 0 && Math.Abs(actualAlpha - alpha) <= 1;
    }

    private bool EnsureJournal()
    {
        if (_journaled) return true;
        if (!_recovery.Save(_target!, _originalExStyle, _originalTopMost, _originalVisible, _originalLayered, _originalAlpha, _originalFlags, _originalColorKey))
        {
            _log.Error("Refused window mutation: recovery journal unavailable.");
            return false;
        }
        _journaled = true;
        return true;
    }

    private void ResetLogicalToOriginal()
    {
        ClickThrough = (_originalExStyle.ToInt64() & Native.WS_EX_TRANSPARENT) != 0;
        Hidden = !_originalVisible;
        Opacity = _originalLayered ? _originalAlpha / 255d : 1;
        TopMost = _originalTopMost;
    }

    private void SyncLogicalFromNative()
    {
        if (!IsAttached) return;
        var hwnd = _target!.Hwnd;
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        ClickThrough = (style & Native.WS_EX_TRANSPARENT) != 0;
        TopMost = (style & Native.WS_EX_TOPMOST) != 0;
        Hidden = !Native.IsWindowVisible(hwnd);
        if ((style & Native.WS_EX_LAYERED) != 0 && Native.GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & Native.LWA_ALPHA) != 0)
            Opacity = alpha / 255d;
    }

    public void Dispose() => Restore();
}
