using System.Windows.Automation;

namespace QwenWorkOverlay;

public sealed class QwenVoiceAutomation
{
    private readonly AppLogger _log;
    public string State { get; private set; } = "Not scanned";
    public string? LastMatchedButton { get; private set; }

    public QwenVoiceAutomation(AppLogger log) => _log = log;

    public bool TryInvokeVoiceButton(IntPtr qwenHwnd)
    {
        if (qwenHwnd == IntPtr.Zero || !Native.IsWindow(qwenHwnd))
        {
            State = "Qwen window unavailable";
            return false;
        }

        try
        {
            var root = AutomationElement.FromHandle(qwenHwnd);
            if (root is null)
            {
                State = "UI Automation root unavailable";
                return false;
            }

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            var candidates = new List<(AutomationElement Element, int Score, string Label)>();
            foreach (AutomationElement element in buttons)
            {
                var name = SafeProperty(element, AutomationElement.NameProperty);
                var help = SafeProperty(element, AutomationElement.HelpTextProperty);
                var automationId = SafeProperty(element, AutomationElement.AutomationIdProperty);
                var combined = $"{name} {help} {automationId}".Trim();
                var score = Score(combined);
                if (score > 0) candidates.Add((element, score, combined));
            }

            var candidate = candidates.OrderByDescending(x => x.Score).FirstOrDefault();
            if (candidate.Element is null)
            {
                State = $"No voice-like button exposed by UI Automation ({buttons.Count} buttons scanned)";
                LastMatchedButton = null;
                return false;
            }

            LastMatchedButton = candidate.Label;
            if (candidate.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) && invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                State = "Voice button invoked through UI Automation";
                _log.Info("Qwen voice automation invoked: " + candidate.Label);
                return true;
            }

            State = "Voice-like button found but it exposes no InvokePattern";
            return false;
        }
        catch (Exception ex)
        {
            State = "Voice automation unavailable: " + ex.GetType().Name;
            _log.Error(State);
            return false;
        }
    }

    public void Probe(IntPtr qwenHwnd)
    {
        if (qwenHwnd == IntPtr.Zero || !Native.IsWindow(qwenHwnd))
        {
            State = "Qwen window unavailable";
            LastMatchedButton = null;
            return;
        }

        try
        {
            var root = AutomationElement.FromHandle(qwenHwnd);
            if (root is null)
            {
                State = "UI Automation root unavailable";
                return;
            }

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var best = new List<(int Score, string Label)>();
            foreach (AutomationElement element in buttons)
            {
                var label = $"{SafeProperty(element, AutomationElement.NameProperty)} {SafeProperty(element, AutomationElement.HelpTextProperty)} {SafeProperty(element, AutomationElement.AutomationIdProperty)}".Trim();
                var score = Score(label);
                if (score > 0) best.Add((score, label));
            }

            var candidate = best.OrderByDescending(x => x.Score).FirstOrDefault();
            LastMatchedButton = candidate.Label;
            State = candidate.Score > 0
                ? "Voice-like button detected"
                : $"No voice-like button exposed ({buttons.Count} buttons scanned)";
        }
        catch (Exception ex)
        {
            State = "Voice automation probe failed: " + ex.GetType().Name;
        }
    }

    private static int Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var value = text.ToLowerInvariant();
        var score = 0;
        foreach (var token in new[] { "microphone", "mic", "voice", "audio", "speech", "record", "麦克风", "语音", "录音", "микрофон", "голос" })
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase)) score += token.Length >= 5 ? 20 : 8;
        if (value.Contains("send", StringComparison.OrdinalIgnoreCase) || value.Contains("submit", StringComparison.OrdinalIgnoreCase)) score -= 20;
        return Math.Max(0, score);
    }

    private static string SafeProperty(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, true);
            return value == AutomationElement.NotSupported ? string.Empty : value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
