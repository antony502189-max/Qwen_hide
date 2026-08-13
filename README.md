# Qwen Desktop Controller

Windows companion controller for the **Qwen Desktop application already installed on your PC**.

It does **not** embed `qwen.ai`, does not create a second Qwen session, and does not replace the Qwen UI. You keep using the same Qwen Desktop app, account, chats, models, file uploads, images, code blocks and built-in voice button.

## What the controller adds

- adjustable opacity on the real Qwen desktop window;
- always-on-top toggle;
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
- best-effort automation of Qwen's existing voice button while Right Ctrl is held;
- system-tray controller and detailed diagnostics;
- verified restoration of Qwen window styles when the controller exits;
- no automatic changes to Windows default or communications audio devices.

## Capture privacy: conservative experimental host

The controller never applies `SetWindowDisplayAffinity` directly to Qwen's foreign top-level HWND. Its optional **Toggle Privacy Host** command instead creates a controller-owned top-level host, verifies `WDA_EXCLUDEFROMCAPTURE` with `GetWindowDisplayAffinity`, then reparents the real installed Qwen window into that host.

Entering this mode requires a verified on-disk recovery record of Qwen's original parent, styles, placement, visibility, topmost state and DPI. A failed affinity/DPI/parenting check rolls back and does not report privacy as enabled. Diagnostics distinguishes verified host affinity from actual capture-pipeline compatibility: Teams, Zoom, Google Meet and Yandex Telemost full-monitor sharing still require the manual checks below.

Current target-machine matrix: host `WDA_EXCLUDEFROMCAPTURE` is read back as `0x11`; GDI has produced both **Exposed** (therefore unsupported) and inconclusive samples; direct `PrintWindow` returned a uniform sample (**Inconclusive**); Desktop Duplication repeatedly produces a **RedactedPlaceholder**; and Windows Graphics Capture has produced **RedactedPlaceholder** and inconclusive samples. A placeholder or uniform render means Qwen content was not observed, not that the host was proven absent. None certifies conferencing applications.

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

Windows CI rejects any reintroduced WebView2 Qwen wrapper, then performs restore, Release build, automated tests, self-contained win-x64 publish, release-payload verification, SHA-256 generation, and artifact upload. Automated tests include real Win32 HWND style/parent recovery; the real-Qwen privacy-host test is opt-in and never runs in CI.

See [GUIDE_EN.md](GUIDE_EN.md), [GUIDE_RU.md](GUIDE_RU.md) and [MANUAL_TEST_CHECKLIST_EN.md](MANUAL_TEST_CHECKLIST_EN.md).
