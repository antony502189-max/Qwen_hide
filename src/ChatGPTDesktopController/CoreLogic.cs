namespace ChatGPTDesktopController;

public static class OpacityPolicy
{
    public const double Minimum = .35;
    public static double Clamp(double value) => Math.Clamp(Math.Round(value, 2), Minimum, 1d);
}

public static class WindowStylePolicy
{
    public static long ComposeVisualStyle(long originalStyle, bool clickThrough)
    {
        var style = originalStyle | Native.WS_EX_LAYERED;
        return clickThrough ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT;
    }
}

public static class VisibilityRestorePolicy
{
    public static int Command(bool wasMinimized, bool wasMaximized) => wasMinimized ? Native.SW_SHOWMINIMIZED : wasMaximized ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOW;
}

public sealed class RightCtrlStateMachine
{
    public bool IsDown { get; private set; }
    public bool OnDown() { if (IsDown) return false; IsDown = true; return true; }
    public bool OnUp() { if (!IsDown) return false; IsDown = false; return true; }
}

public static class ModifierReleasePolicy
{
    public static bool Released(bool ctrl, bool alt, bool v) => !ctrl && !alt && !v;
}

public static class AudioEndpointSafety
{
    public static bool CanStart(string? physicalMicId, string? virtualOutputId, bool rightCtrlEnabled) => rightCtrlEnabled && !string.IsNullOrWhiteSpace(physicalMicId) && !string.IsNullOrWhiteSpace(virtualOutputId) && !string.Equals(physicalMicId, virtualOutputId, StringComparison.OrdinalIgnoreCase);
}

public static class VoiceShortcutPolicy
{
    public static bool CanInvoke(bool nativeShortcutDiscovered, string? shortcut) => nativeShortcutDiscovered && !string.IsNullOrWhiteSpace(shortcut);
}

public static class VoiceControlPolicy
{
    public static bool NameLooksLikeVoiceControl(string? name) => Score(name) > 0;

    // We want speech-to-text dictation first. Full voice mode is useful, but it changes the
    // conversation UX and therefore must not win just because it happens to appear first in UIA.
    public static int Score(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var text = name.ToLowerInvariant();
        if (text.Contains("dictat") || text.Contains("диктов")) return 300;
        if ((text.Contains("voice") || text.Contains("голос")) && (text.Contains("mode") || text.Contains("режим"))) return 200;
        if (text.Contains("voice") || text.Contains("голос") || text.Contains("microphone") || text.Contains("микроф")) return 100;
        return 0;
    }
}

public static class ComposerControlPolicy
{
    public static int Score(string? automationId, string? name, string? helpText, bool isEdit)
    {
        if (string.Equals(automationId, "prompt-textarea", StringComparison.OrdinalIgnoreCase)) return 1000;
        var label = ((name ?? string.Empty) + " " + (helpText ?? string.Empty)).ToLowerInvariant();
        if (label.Contains("message") || label.Contains("сообщ") || label.Contains("prompt") || label.Contains("chat") || label.Contains("чат")) return isEdit ? 500 : 400;
        return isEdit ? 100 : 10;
    }
}
