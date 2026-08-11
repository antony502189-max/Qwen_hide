namespace QwenWorkOverlay;

public static class VirtualMixOutputPolicy
{
    public static bool IsRecognizedVirtualName(string? name) => !string.IsNullOrWhiteSpace(name) && System.Text.RegularExpressions.Regex.IsMatch(name, "virtual|cable|voicemeeter|loopback|blackhole", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

public enum ClipboardPayloadKind { Empty, Text, Image }
public static class ClipboardPolicy
{
    public static ClipboardPayloadKind Classify(bool hasText, bool hasImage) => hasText ? ClipboardPayloadKind.Text : hasImage ? ClipboardPayloadKind.Image : ClipboardPayloadKind.Empty;
}
public sealed class RightCtrlStateMachine
{
    public bool IsDown { get; private set; }
    public bool OnDown() { if (IsDown) return false; IsDown=true; return true; }
    public bool OnUp() { if (!IsDown) return false; IsDown=false; return true; }
}
public readonly record struct CaptureProtectionResult(bool Requested, bool ApiSucceeded)
{
    public bool IsProtected => Requested && ApiSucceeded;
    public string Status => IsProtected ? "ON" : Requested ? "FAILED" : "OFF";
}
