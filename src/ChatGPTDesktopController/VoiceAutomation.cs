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
            var hasVoiceControl = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Any(x => (x.Current.Name + x.Current.HelpText).Contains("voice", StringComparison.OrdinalIgnoreCase));
            Status = new(false, "Not discovered", hasVoiceControl ? "Voice UI exposed, but no native shortcut discovered" : "No voice shortcut/control exposed by UIA", "Disabled until a native shortcut is verified");
        }
        catch (Exception ex) { Status = new(false, "Not discovered", "Probe failed: " + ex.GetType().Name, "Disabled"); }
    }
    public void Invoke(ChatGPTTarget? target)
    {
        // Intentionally fail closed. No undocumented shortcut and no geometry click are sent.
        Status = Status with { LastInvocation = Status.NativeShortcutDiscovered ? "Not implemented" : "Skipped: no verified native shortcut" };
        _log.Info("Voice invocation " + Status.LastInvocation);
    }
}
