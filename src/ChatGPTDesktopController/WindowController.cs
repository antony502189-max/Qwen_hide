namespace ChatGPTDesktopController;

public sealed class WindowController : IDisposable
{
    private readonly AppLogger _log;
    private readonly RecoveryService _recovery;
    private ChatGPTTarget? _target;
    private IntPtr _originalExStyle;
    private bool _originalTopMost, _originalVisible, _originalLayered, _journaled, _hiddenFromMinimized, _hiddenFromMaximized;
    private byte _originalAlpha = 255;
    private uint _originalFlags = Native.LWA_ALPHA, _originalColorKey;
    public ChatGPTTarget? Target => _target;
    public bool IsAttached => _target is { } t && Native.IsWindow(t.Hwnd);
    public bool ClickThrough { get; private set; }
    public bool TopMost { get; private set; }
    public bool Hidden { get; private set; }
    public double Opacity { get; private set; } = 1;
    public WindowController(AppLogger log, RecoveryService recovery) { _log = log; _recovery = recovery; }
    public bool Attach(ChatGPTTarget target)
    {
        if (IsAttached && _target!.Hwnd == target.Hwnd) return true;
        Restore(); _target = null; _journaled = false;
        if (!Native.IsWindow(target.Hwnd)) return false;
        _target = target; _originalExStyle = Native.GetWindowLongPtr(target.Hwnd, Native.GWL_EXSTYLE);
        _originalTopMost = (_originalExStyle.ToInt64() & Native.WS_EX_TOPMOST) != 0; _originalVisible = Native.IsWindowVisible(target.Hwnd);
        _originalLayered = (_originalExStyle.ToInt64() & Native.WS_EX_LAYERED) != 0;
        if (_originalLayered && Native.GetLayeredWindowAttributes(target.Hwnd, out var key, out var alpha, out var flags)) { _originalColorKey = key; _originalAlpha = alpha; _originalFlags = flags; }
        TopMost = _originalTopMost; Hidden = !_originalVisible; ClickThrough = (_originalExStyle.ToInt64() & Native.WS_EX_TRANSPARENT) != 0; Opacity = _originalLayered ? _originalAlpha / 255d : 1;
        _log.Info("Attached " + target.Summary); return true;
    }
    public bool ToggleVisibility() => Mutate(() =>
    {
        if (!Hidden)
        {
            _hiddenFromMinimized = Native.IsIconic(_target!.Hwnd); _hiddenFromMaximized = Native.IsZoomed(_target.Hwnd);
            Native.ShowWindow(_target.Hwnd, Native.SW_HIDE); Hidden = true;
        }
        else
        {
            Native.ShowWindow(_target!.Hwnd, VisibilityRestorePolicy.Command(_hiddenFromMinimized, _hiddenFromMaximized)); Hidden = false;
        }
        return Native.IsWindowVisible(_target!.Hwnd) != Hidden;
    });
    public bool ToggleClickThrough() => Mutate(() => { ClickThrough = !ClickThrough; ApplyVisuals(); return true; });
    public bool ToggleTopMost() => Mutate(() => { TopMost = !TopMost; return Native.SetWindowPos(_target!.Hwnd, TopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED); });
    public bool AdjustOpacity(double delta) => SetOpacity(Opacity + delta);
    public bool SetOpacity(double value) => Mutate(() => { Opacity = OpacityPolicy.Clamp(value); ApplyVisuals(); return true; });
    public bool EnsureInteractive(Action operation)
    {
        if (!IsAttached) return false;
        if (!EnsureJournal()) return false;
        var prior = ClickThrough;
        try { if (prior) { ClickThrough = false; ApplyVisuals(); } operation(); return true; }
        finally { if (prior && IsAttached) { ClickThrough = true; ApplyVisuals(); } }
    }
    public bool Restore()
    {
        if (!_journaled || _target is not { } t || !Native.IsWindow(t.Hwnd)) return true;
        var result = _recovery.TryRecoverStaleState(); if (result) { _journaled = false; ClickThrough = false; Hidden = false; Opacity = 1; TopMost = false; }
        return result;
    }
    private bool Mutate(Func<bool> action)
    {
        if (!IsAttached) return false;
        if (!EnsureJournal()) return false;
        try { return action(); } catch (Exception ex) { _log.Error("Window mutation failed: " + ex.GetType().Name); _recovery.TryRecoverStaleState(); _journaled = false; return false; }
    }
    private void ApplyVisuals()
    {
        var style = WindowStylePolicy.ComposeVisualStyle(_originalExStyle.ToInt64(), ClickThrough);
        Native.SetWindowLongPtr(_target!.Hwnd, Native.GWL_EXSTYLE, new IntPtr(style));
        Native.SetLayeredWindowAttributes(_target.Hwnd, 0, (byte)Math.Round(Opacity * 255), Native.LWA_ALPHA);
        Native.SetWindowPos(_target.Hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }
    private bool EnsureJournal()
    {
        if (_journaled) return true;
        if (!_recovery.Save(_target!, _originalExStyle, _originalTopMost, _originalVisible, _originalLayered, _originalAlpha, _originalFlags, _originalColorKey)) { _log.Error("Refused window mutation: recovery journal unavailable."); return false; }
        _journaled = true;
        return true;
    }
    public void Dispose() => Restore();
}
