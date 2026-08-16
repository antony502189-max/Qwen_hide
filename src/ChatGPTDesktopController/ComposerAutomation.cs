using System.Diagnostics;
using System.Runtime.InteropServices;
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

        if (!await WaitForHotkeyReleaseAsync())
            return Fail("GetAsyncKeyState", "Ctrl/Alt/V were still physically down after the release timeout; paste cancelled");

        Set(PasteStage.ModifiersReleased, "GetAsyncKeyState", "Ctrl/Alt/V released");

        if (target is null || !Native.IsWindow(target.Hwnd))
            return Fail("target", "ChatGPT Classic not attached");

        Set(PasteStage.TargetResolved, "validated package", target.Summary);
        PasteResult? result = null;
        var prepared = window.EnsureInteractive(() => result = FocusAndPaste(target));
        return prepared ? result ?? Fail("window", "Paste operation returned no result") : Fail("window", "Could not prepare interactive target state");
    }

    private PasteResult FocusAndPaste(ChatGPTTarget target)
    {
        if (!Native.TryActivateWindow(target.Hwnd))
            return Fail("Win32 activation", "Could not activate ChatGPT Classic");
        Set(PasteStage.Activated, "Win32 activation", "Target foreground handoff completed");

        if (!TryFocusComposer(target.Hwnd, out var method, out var why))
            return Fail(method, why);
        Set(PasteStage.ComposerFocused, method, why);

        if (!ScreenshotService.ClipboardContainsImage())
            return Fail(method, "Clipboard does not contain an image");
        Set(PasteStage.ClipboardValidated, method, "Clipboard contains image");

        if (!SendCtrlV())
            return Fail("SendInput", "Windows rejected one or more Ctrl+V input events; win32=" + Marshal.GetLastPInvokeError());

        Set(PasteStage.PasteSent, "SendInput", "Ctrl+V accepted by SendInput after modifier release");
        Thread.Sleep(120);
        Set(PasteStage.Completed, method, "Paste input dispatched successfully; ChatGPT owns attachment rendering");
        return LastResult;
    }

    private static bool TryFocusComposer(IntPtr hwnd, out string method, out string detail)
    {
        method = "UI Automation";
        detail = "No editable/document composer exposed";

        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));

            var candidates = root.FindAll(TreeScope.Descendants, condition)
                .Cast<AutomationElement>()
                .Where(IsUsable)
                .Select(x => new
                {
                    Element = x,
                    Score = ComposerControlPolicy.Score(
                        Safe(() => x.Current.AutomationId),
                        Safe(() => x.Current.Name),
                        Safe(() => x.Current.HelpText),
                        x.Current.ControlType == ControlType.Edit)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var selected = candidates.FirstOrDefault();
            if (selected is null) return false;

            selected.Element.SetFocus();
            Thread.Sleep(40);

            var automationId = Safe(() => selected.Element.Current.AutomationId);
            var name = Safe(() => selected.Element.Current.Name);
            method = string.Equals(automationId, "prompt-textarea", StringComparison.OrdinalIgnoreCase)
                ? "UI Automation prompt-textarea"
                : "UI Automation " + selected.Element.Current.ControlType.ProgrammaticName;
            detail = $"Focused composer: id={automationId}; name={name}; score={selected.Score}";
            return true;
        }
        catch (ElementNotAvailableException)
        {
            detail = "Accessibility element disappeared";
            return false;
        }
        catch (Exception ex)
        {
            detail = "UI Automation: " + ex.GetType().Name;
            return false;
        }
    }

    private static bool IsUsable(AutomationElement element)
    {
        try { return element.Current.IsEnabled && element.Current.IsKeyboardFocusable; }
        catch (ElementNotAvailableException) { return false; }
    }

    private static string Safe(Func<string> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static async Task<bool> WaitForHotkeyReleaseAsync()
    {
        var until = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 1.5);
        while (Stopwatch.GetTimestamp() < until)
        {
            if (ModifierReleasePolicy.Released(Native.IsKeyDown(0x11), Native.IsKeyDown(0x12), Native.IsKeyDown(0x56))) return true;
            await Task.Delay(10);
        }
        return ModifierReleasePolicy.Released(Native.IsKeyDown(0x11), Native.IsKeyDown(0x12), Native.IsKeyDown(0x56));
    }

    private static bool SendCtrlV()
    {
        var inputs = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
        Marshal.SetLastPInvokeError(0);
        return Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>()) == inputs.Length;
    }

    private static Native.INPUT Key(ushort vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { Ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 } }
    };

    private PasteResult Fail(string method, string detail)
    {
        Set(PasteStage.Failed, method, detail);
        return LastResult;
    }

    private void Set(PasteStage stage, string method, string detail)
    {
        LastResult = new(stage, method, detail, DateTimeOffset.Now);
        _log.Info($"Paste {stage}: {method}; {detail}");
    }
}
