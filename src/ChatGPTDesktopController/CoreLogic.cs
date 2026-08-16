namespace ChatGPTDesktopController;

public static class OpacityPolicy
{
    public const double Minimum = .35;
    public static double Clamp(double value) => Math.Clamp(Math.Round(value, 2), Minimum, 1d);
}
public static class WindowStylePolicy
{
    public static long ComposeVisualStyle(long originalStyle, bool clickThrough) => (originalStyle | Native.WS_EX_LAYERED) | (clickThrough ? Native.WS_EX_TRANSPARENT : 0);
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
