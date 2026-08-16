using System.Diagnostics;
using System.Windows.Automation;

namespace ChatGPTDesktopController;

public enum PasteStage { Idle, TriggerReceived, ModifiersReleased, TargetResolved, Activated, ComposerFocused, ClipboardValidated, PasteSent, Completed, Failed }
public sealed record PasteResult(PasteStage Stage, string Method, string Detail, DateTimeOffset At);

public sealed class ComposerAutomation
{
    private readonly AppLogger _log;
    public PasteResult LastResult { get; private set; } = new(PasteStage.Idle, "none", "Not run", DateTimeOffset.MinValue);
    public ComposerAutomation(AppLogger log) => _log = log;

    public async Task<PasteResult> PasteImageAsync(ChatGPTTarget? target, WindowController window)
    {
        Set(PasteStage.TriggerReceived, "none", "Ctrl+Alt+V received");
        await WaitForHotkeyReleaseAsync(); Set(PasteStage.ModifiersReleased, "GetAsyncKeyState", "Ctrl/Alt/V released");
        if (target is null || !Native.IsWindow(target.Hwnd)) return Fail("target", "ChatGPT Classic not attached");
        Set(PasteStage.TargetResolved, "validated package", target.Summary);
        PasteResult? result = null;
        window.EnsureInteractive(() => result = FocusAndPaste(target));
        return result ?? Fail("window", "Could not prepare interactive target state");
    }
    private PasteResult FocusAndPaste(ChatGPTTarget target)
    {
        if (Native.IsIconic(target.Hwnd)) Native.ShowWindow(target.Hwnd, Native.SW_RESTORE);
        Activate(target.Hwnd); Set(PasteStage.Activated, "Win32 activation", "Target foreground handoff requested");
        if (!TryFocusComposer(target.Hwnd, out var method, out var why)) return Fail(method, why);
        Set(PasteStage.ComposerFocused, method, "Accessible composer focused");
        if (!ScreenshotService.ClipboardContainsImage()) return Fail(method, "Clipboard does not contain an image");
        Set(PasteStage.ClipboardValidated, method, "Clipboard contains image");
        SendCtrlV(); Set(PasteStage.PasteSent, "SendInput", "Ctrl+V generated after modifier release");
        Thread.Sleep(120); Set(PasteStage.Completed, method, "Paste command completed; target UI owns attachment rendering"); return LastResult;
    }
    private static void Activate(IntPtr hwnd)
    {
        var foreground = Native.GetForegroundWindow(); var sourceThread = Native.GetWindowThreadProcessId(foreground, IntPtr.Zero); var targetThread = Native.GetWindowThreadProcessId(hwnd, IntPtr.Zero); var current = Native.GetCurrentThreadId();
        try { if (sourceThread != 0 && sourceThread != current) Native.AttachThreadInput(current, sourceThread, true); if (targetThread != 0 && targetThread != current) Native.AttachThreadInput(current, targetThread, true); Native.BringWindowToTop(hwnd); Native.SetForegroundWindow(hwnd); }
        finally { if (sourceThread != 0 && sourceThread != current) Native.AttachThreadInput(current, sourceThread, false); if (targetThread != 0 && targetThread != current) Native.AttachThreadInput(current, targetThread, false); }
    }
    private static bool TryFocusComposer(IntPtr hwnd, out string method, out string detail)
    {
        method = "UI Automation"; detail = "No editable/document composer exposed";
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var candidates = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()
                .Where(x => x.Current.IsEnabled && x.Current.IsKeyboardFocusable)
                .OrderByDescending(Score).ToList();
            var composer = candidates.FirstOrDefault();
            if (composer is null) return false;
            composer.SetFocus();
            method = "UI Automation " + composer.Current.ControlType.ProgrammaticName;
            detail = "Focused: " + composer.Current.Name;
            return true;
        }
        catch (ElementNotAvailableException) { detail = "Accessibility element disappeared"; return false; }
        catch (Exception ex) { detail = "UI Automation: " + ex.GetType().Name; return false; }
    }
    private static int Score(AutomationElement x)
    {
        var label = (x.Current.Name + " " + x.Current.AutomationId + " " + x.Current.HelpText).ToLowerInvariant();
        return (label.Contains("message") || label.Contains("сообщ") || label.Contains("prompt") || label.Contains("chat")) ? 100 : x.Current.ControlType == ControlType.Edit ? 10 : 0;
    }
    private static async Task WaitForHotkeyReleaseAsync()
    {
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency; // at most one second, independent of normal hotkeys
        // The caller is the WPF dispatcher. Preserve that STA context because Clipboard and UIA
        // work below must not resume on a thread-pool thread after the modifier-release wait.
        while ((Native.IsKeyDown(0x11) || Native.IsKeyDown(0x12) || Native.IsKeyDown(0x56)) && Stopwatch.GetTimestamp() < until) await Task.Delay(10);
    }
    private static void SendCtrlV()
    {
        var inputs = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
        Native.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }
    private static Native.INPUT Key(ushort vk, bool up) => new() { type = Native.INPUT_KEYBOARD, U = new Native.InputUnion { Ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 } } };
    private PasteResult Fail(string method, string detail) { Set(PasteStage.Failed, method, detail); return LastResult; }
    private void Set(PasteStage stage, string method, string detail) { LastResult = new(stage, method, detail, DateTimeOffset.Now); _log.Info($"Paste {stage}: {method}; {detail}"); }
}
