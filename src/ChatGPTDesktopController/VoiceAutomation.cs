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
        if (target is null)
        {
            Status = new(false, "Not discovered", "Target not attached", "No coordinate fallback is used");
            return;
        }

        try
        {
            var controls = FindVoiceControls(target.Hwnd);
            var selected = controls.OrderByDescending(x => VoiceControlPolicy.Score(x.Name)).FirstOrDefault();
            if (selected is null)
            {
                Status = new(false, "No native shortcut discovered", "No accessible voice control exposed by UIA", "Disabled; no coordinate fallback is used");
                return;
            }

            Status = new(false, "No native shortcut; UIA control: " + selected.Name, "Ready: deterministic accessible voice action selected", "UI Automation InvokePattern; no coordinates");
        }
        catch (Exception ex)
        {
            Status = new(false, "Not discovered", "Probe failed: " + ex.GetType().Name, "Disabled; no coordinate fallback is used");
        }
    }

    public void Invoke(ChatGPTTarget? target)
    {
        if (target is null || !Native.IsWindow(target.Hwnd))
        {
            Status = Status with { LastInvocation = "Skipped: target not attached" };
            return;
        }

        try
        {
            if (!Native.TryActivateWindow(target.Hwnd))
            {
                Status = Status with { LastInvocation = "Skipped: ChatGPT could not be activated", FallbackState = "No coordinate fallback is used" };
                return;
            }

            var controls = FindVoiceControls(target.Hwnd);
            var selected = controls.OrderByDescending(x => VoiceControlPolicy.Score(x.Name)).FirstOrDefault();
            if (selected is null)
            {
                Status = Status with { LastInvocation = "Skipped: no accessible voice control", FallbackState = "No coordinate fallback is used" };
                return;
            }

            if (!selected.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                Status = Status with { LastInvocation = "Skipped: selected voice control has no InvokePattern: " + selected.Name, FallbackState = "No coordinate fallback is used" };
                return;
            }

            ((InvokePattern)pattern).Invoke();
            Status = new(false, "No native shortcut; UIA control: " + selected.Name, "UI Automation invoked: " + selected.Name, "Accessible InvokePattern; no coordinates");
        }
        catch (Exception ex)
        {
            Status = Status with { LastInvocation = "Voice invoke failed: " + ex.GetType().Name, FallbackState = "No coordinate fallback is used" };
        }

        _log.Info("Voice invocation " + Status.LastInvocation);
    }

    private static List<VoiceControlCandidate> FindVoiceControls(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var result = new List<VoiceControlCandidate>();

        foreach (AutomationElement element in buttons)
        {
            try
            {
                if (!element.Current.IsEnabled) continue;
                var name = (element.Current.Name + " " + element.Current.HelpText).Trim();
                if (VoiceControlPolicy.Score(name) <= 0) continue;
                result.Add(new VoiceControlCandidate(element, name));
            }
            catch (ElementNotAvailableException) { }
        }

        return result;
    }

    private sealed record VoiceControlCandidate(AutomationElement Element, string Name);
}
