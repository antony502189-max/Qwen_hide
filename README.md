# Qwen Desktop Controller

Windows companion controller for the **Qwen Desktop application already installed on your PC**.

It does **not** embed `qwen.ai`, does not create a second Qwen session, and does not replace the Qwen UI. You keep using the same Qwen Desktop app, account, chats, models, file uploads, images, code blocks and built-in voice button.

## What the controller adds

- opt-in opacity on the real Qwen desktop window (disabled until a target-machine compositor check passes);
- opt-in always-on-top toggle;
- reversible mouse click-through mode;
- global hide/show hotkey;
- emergency `Ctrl+Alt+Esc` restore-and-exit path;
- crash-recovery journal for the original native Qwen window style;
- automatic re-attach after Qwen restarts;
- single-controller-instance guard;
- active-window and monitor screenshots directly to the Windows Clipboard;
- Chromium/Electron black-frame detection with screen-copy fallback;
- Clipboard → existing Qwen paste helper;
- shared-mode physical microphone capture;
- WASAPI loopback capture of Windows playback;
- optional mic + system-audio mix sent only to a recognized virtual cable;
- bounded audio queues, format conversion/resampling and callback-generation guards;
- calibrated-message voice toggle for Qwen's existing voice button;
- system-tray controller and detailed diagnostics;
- verified restoration of Qwen window styles when the controller exits;
- no automatic changes to Windows default or communications audio devices.

## Capture privacy: unsupported for this native Qwen build

Windows documents [`SetWindowDisplayAffinity`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) as an API the owning process applies to its own top-level window. This controller does not inject into, patch, or modify the installed Qwen process, so it cannot safely apply that API to Qwen's foreign HWND.

The previously tested controller-owned host plus cross-process `SetParent` architecture is **hard-disabled**. On this target Qwen 1.0.3 did not track host resizing after reparenting; keeping that path would risk a broken or laggy Chromium window. Therefore same-monitor full-share exclusion is currently **UNSUPPORTED ON TARGET MACHINE**. The controller will not claim protection for GDI, Desktop Duplication, Windows Graphics Capture, Teams, Zoom, Google Meet, or Yandex Telemost.

## Quick start

```powershell
cd E:\qwen_hide
.\scripts\setup.ps1
.\scripts\build.ps1
.\dist\QwenDesktopController.exe
```

Open the normal Qwen Desktop app if it is not already running. The controller will attach to its native top-level window and can then minimize itself to the system tray.

For exact target-machine diagnostics with Qwen open:

```powershell
.\scripts\runtime-probe.ps1
```

This writes `artifacts\runtime-probe.json` without collecting chat text, Clipboard contents, credentials or audio.

## Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+Q` | Hide/show native Qwen |
| `Ctrl+Alt+X` | Click-through ON/OFF |
| `Ctrl+Alt+T` | TopMost ON/OFF |
| `Ctrl+Alt+Up/Down` | Opacity ±5% |
| `Ctrl+Alt+V` | Paste Clipboard into Qwen |
| `Ctrl+Alt+D` | Diagnostics |
| `F6` | Last non-Qwen work window → Clipboard |
| `Shift+F6` | Current monitor → Clipboard |
| `Ctrl+Shift+R` | Toggle Qwen voice recording |
| hold `Right Ctrl` | Run configured Qwen-only audio mix |
| `Ctrl+Alt+Esc` | Emergency restore Qwen and exit controller |

## Quality gates

Windows CI rejects any reintroduced WebView2 Qwen wrapper, then performs restore, Release build, automated tests, self-contained win-x64 publish, release-payload verification, SHA-256 generation, and artifact upload. Automated tests include real Win32 HWND recovery; real-Qwen mutation tests are opt-in and never run in CI.

See [GUIDE_EN.md](GUIDE_EN.md), [GUIDE_RU.md](GUIDE_RU.md) and [MANUAL_TEST_CHECKLIST_EN.md](MANUAL_TEST_CHECKLIST_EN.md).
