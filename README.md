# Qwen Desktop Controller

Windows companion controller for the **Qwen Desktop application already installed on your PC**.

It does **not** embed `qwen.ai`, does not create a second Qwen session, and does not replace the Qwen UI. You keep using the same Qwen Desktop app, account, chats, models, file uploads, images, code blocks and built-in voice button.

## What the controller adds

- adjustable opacity on the real Qwen desktop window;
- always-on-top toggle;
- reversible mouse click-through mode;
- global hide/show hotkey;
- active-window and monitor screenshots directly to the Windows Clipboard;
- Clipboard → existing Qwen paste helper;
- shared-mode physical microphone capture;
- WASAPI loopback capture of Windows playback;
- optional mic + system-audio mix sent only to a recognized virtual cable;
- best-effort automation of Qwen's existing voice button while Right Ctrl is held;
- system-tray controller and diagnostics;
- restoration of Qwen window styles when the controller exits;
- no automatic changes to Windows default or communications audio devices.

## Important capture-privacy limitation

The installed Qwen window belongs to the Qwen process, not this controller. The safe native mode therefore does **not** claim `WDA_EXCLUDEFROMCAPTURE` support for the Qwen window. The controller deliberately avoids DLL injection, binary patching or replacing Qwen with an embedded web client just to fake this checkbox.

If you need privacy during a work share, prefer sharing a specific application/window rather than the whole desktop. Full-screen capture behavior must be tested with the exact conferencing software you use.

## Quick start

```powershell
cd E:\qwen_hide
.\scripts\setup.ps1
.\scripts\build.ps1
.\dist\QwenDesktopController.exe
```

Open the normal Qwen Desktop app if it is not already running. The controller will attach to its native top-level window and can then minimize itself to the system tray.

See [GUIDE_EN.md](GUIDE_EN.md) and [MANUAL_TEST_CHECKLIST_EN.md](MANUAL_TEST_CHECKLIST_EN.md).
