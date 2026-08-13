using System.Windows.Automation;

namespace QwenWorkOverlay;

public sealed class QwenVoiceAutomation
{
    private const int MinimumInvokeScore = 20;
    private readonly AppLogger _log;
    private readonly QwenVoiceClickFallback _clickFallback;

    public string State { get; private set; } = "Not scanned";
    public string? LastMatchedButton { get; private set; }
    public int LastMatchedScore { get; private set; }
    public string ClickFallbackStatus => _clickFallback.Status;

    public QwenVoiceAutomation(AppLogger log)
    {
        _log = log;
        _clickFallback = new QwenVoiceClickFallback(log);
    }

    public bool TryInvokeVoiceButton(IntPtr qwenHwnd)
    {
        // This Qwen build has a known-empty UIA/MSAA composer tree. Prefer the validated
        // geometry-relative calibration when it exists, instead of repeatedly rescanning it.
        if (_clickFallback.HasCalibration)
        {
            if (_clickFallback.TryInvoke(qwenHwnd, out var clickDiagnostic))
            {
                LastMatchedButton = "calibrated composer click";
                LastMatchedScore = 0;
                State = "Voice toggled through calibrated click fallback";
                return true;
            }

            State = clickDiagnostic;
            return false;
        }

        if (!TryFindCandidate(qwenHwnd, out var candidate, out var diagnostic))
        {
            State = diagnostic + "; calibrated click fallback not configured";
            return false;
        }

        LastMatchedButton = candidate.Label;
        LastMatchedScore = candidate.Score;
        if (candidate.Score < MinimumInvokeScore)
        {
            State = $"Voice candidate confidence too low ({candidate.Score}); manual Qwen voice use required";
            _log.Info(State + ": " + candidate.Label);
            return false;
        }

        if (candidate.Ambiguous)
        {
            State = "Voice automation refused an ambiguous accessibility match; manual Qwen voice use required";
            _log.Info(State + ": " + candidate.Label);
            return false;
        }

        try
        {
            if (candidate.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) && invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                State = $"Voice button invoked through UI Automation (confidence {candidate.Score})";
                _log.Info("Qwen voice automation invoked: " + candidate.Label);
                return true;
            }

            State = "Voice-like button found but it exposes no InvokePattern";
            return false;
        }
        catch (Exception ex)
        {
            State = "Voice automation invocation failed: " + ex.GetType().Name;
            _log.Error(State);
            return false;
        }
    }

    public void Probe(IntPtr qwenHwnd)
    {
        if (_clickFallback.HasCalibration)
        {
            LastMatchedButton = "calibrated composer click";
            LastMatchedScore = 0;
            State = "UI Automation discovery skipped; calibrated click fallback READY";
            return;
        }

        if (!TryFindCandidate(qwenHwnd, out var candidate, out var diagnostic))
        {
            LastMatchedButton = null;
            LastMatchedScore = 0;
            State = diagnostic + "; calibrated click fallback not configured";
            return;
        }

        LastMatchedButton = candidate.Label;
        LastMatchedScore = candidate.Score;
        State = candidate.Ambiguous
            ? $"Ambiguous voice-like controls detected (top confidence {candidate.Score})"
            : candidate.Score >= MinimumInvokeScore
                ? $"Voice-like button detected (confidence {candidate.Score})"
                : $"Low-confidence voice-like control detected ({candidate.Score}); manual mode recommended";
    }

    private static bool TryFindCandidate(
        IntPtr qwenHwnd,
        out (AutomationElement Element, int Score, string Label, bool Ambiguous) candidate,
        out string diagnostic)
    {
        candidate = default;
        if (qwenHwnd == IntPtr.Zero || !Native.IsWindow(qwenHwnd))
        {
            diagnostic = "Qwen window unavailable";
            return false;
        }

        try
        {
            var root = AutomationElement.FromHandle(qwenHwnd);
            if (root is null)
            {
                diagnostic = "UI Automation root unavailable";
                return false;
            }

            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            var candidates = new List<(AutomationElement Element, int Score, string Label)>();
            foreach (AutomationElement element in buttons)
            {
                if (!IsUsable(element)) continue;
                var name = SafeProperty(element, AutomationElement.NameProperty);
                var help = SafeProperty(element, AutomationElement.HelpTextProperty);
                var automationId = SafeProperty(element, AutomationElement.AutomationIdProperty);
                var combined = $"{name} {help} {automationId}".Trim();
                var score = Score(combined);
                if (score > 0) candidates.Add((element, score, combined));
            }

            var ordered = candidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ordered.Count == 0)
            {
                diagnostic = $"No usable voice-like button exposed by UI Automation ({buttons.Count} buttons scanned)";
                return false;
            }

            var best = ordered[0];
            var ambiguous = ordered.Count > 1 && ordered[1].Score == best.Score &&
                            !string.Equals(ordered[1].Label, best.Label, StringComparison.OrdinalIgnoreCase);
            candidate = (best.Element, best.Score, best.Label, ambiguous);
            diagnostic = "Voice candidate found";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "Voice automation probe failed: " + ex.GetType().Name;
            return false;
        }
    }

    private static bool IsUsable(AutomationElement element)
    {
        try
        {
            var enabled = element.GetCurrentPropertyValue(AutomationElement.IsEnabledProperty, true);
            var offscreen = element.GetCurrentPropertyValue(AutomationElement.IsOffscreenProperty, true);
            if (enabled is bool isEnabled && !isEnabled) return false;
            if (offscreen is bool isOffscreen && isOffscreen) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static int Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var value = text.Trim().ToLowerInvariant();
        var score = 0;

        if (value is "mic" or "microphone" or "voice" or "микрофон" or "голос" or "麦克风" or "语音") score += 35;

        foreach (var token in new[] { "microphone", "voice input", "voice", "speech", "dictation", "record", "麦克风", "语音", "录音", "микрофон", "голос", "диктов" })
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 20;

        if (value.Contains(" mic", StringComparison.OrdinalIgnoreCase) || value.StartsWith("mic", StringComparison.OrdinalIgnoreCase)) score += 12;
        if (value.Contains("audio input", StringComparison.OrdinalIgnoreCase)) score += 18;

        foreach (var negative in new[]
        {
            "send", "submit", "speaker", "audio output", "output device", "volume", "settings", "model", "camera", "video", "stop generating", "playback"
        })
            if (value.Contains(negative, StringComparison.OrdinalIgnoreCase)) score -= 25;

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
