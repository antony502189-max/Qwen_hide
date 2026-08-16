using System.Windows.Automation;

namespace ChatGPTDesktopController;

public sealed record VoiceStatus(bool NativeShortcutDiscovered, string Shortcut, string LastInvocation, string FallbackState);

public sealed class VoiceAutomation
{
    private readonly AppLogger _log;
    public VoiceStatus Status { get; private set; } = new(false, "Not discovered", "Not invoked", "No coordinate fallback is used");
    public VoiceAutomation(AppLogger log) => _log = log;
    public void Probe(ChatGPTTarget? target)
    {
        // This is observational: UIA is queried only when diagnostics/probe requests it. A microphone button is not evidence of a keyboard shortcut.
        if (target is null) { Status = new(false, "Not discovered", "Target not attached", "No coordinate fallback is used"); return; }
        try
        {
            var root = AutomationElement.FromHandle(target.Hwnd);
            var hasVoiceControl = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Any(x => VoiceControlPolicy.NameLooksLikeVoiceControl(x.Current.Name + " " + x.Current.HelpText));
            Status = new(false, "Not discovered", hasVoiceControl ? "Voice UI exposed, but no native shortcut discovered" : "No voice shortcut/control exposed by UIA", "Disabled until a native shortcut is verified");
        }
        catch (Exception ex) { Status = new(false, "Not discovered", "Probe failed: " + ex.GetType().Name, "Disabled"); }
    }
    public void Invoke(ChatGPTTarget? target)
    {
        if (target is null || !Native.IsWindow(target.Hwnd)) { Status = Status with { LastInvocation = "Skipped: target not attached" }; return; }
        try
        {
            if (Native.IsIconic(target.Hwnd)) Native.ShowWindow(target.Hwnd, Native.SW_RESTORE);
            Native.SetForegroundWindow(target.Hwnd);
            var root = AutomationElement.FromHandle(target.Hwnd);
            var button = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .FirstOrDefault(x => x.Current.IsEnabled && VoiceControlPolicy.NameLooksLikeVoiceControl(x.Current.Name + " " + x.Current.HelpText));
            if (button is null) { Status = Status with { LastInvocation = "Skipped: no accessible voice control", FallbackState = "No coordinate fallback is used" }; return; }
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) { Status = Status with { LastInvocation = "Skipped: voice control has no InvokePattern", FallbackState = "No coordinate fallback is used" }; return; }
            ((InvokePattern)pattern).Invoke();
            Status = Status with { LastInvocation = "UI Automation invoked: " + button.Current.Name, FallbackState = "Accessible InvokePattern; no coordinates" };
        }
        catch (Exception ex) { Status = Status with { LastInvocation = "Voice invoke failed: " + ex.GetType().Name, FallbackState = "No coordinate fallback is used" }; }
        _log.Info("Voice invocation " + Status.LastInvocation);
    }
}
