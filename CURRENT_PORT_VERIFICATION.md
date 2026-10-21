# Current application port verification

Observed on Windows x64 with OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0, 20 October 2026. These observations apply to that installation, rather than all builds or capture applications.

- 44 automated tests passed: existing logic, privacy transitions and current-package selection.
- Full-mode hotkeys registered without reported conflicts. Native WM_HOTKEY handler checks covered hide/show, topmost, click-through, opacity and emergency recovery. These were handler checks, not physical keyboard tests of every shortcut.
- Both installed desktop shortcuts were exercised: enable started one controller; disable stopped it gracefully; enable restarted it. Final main-window affinity was 0x11 with ordinary opacity/input restored.
- PrintWindow produced a blank main-window capture. GDI monitor capture matched the underlying scene with a contrasting control. Windows Graphics Capture also observed exclusion with a valid control.
- The latest DXGI control was flat, so the result was inconclusive.
- Nineteen of twenty enumerated app windows reported affinity 0x11. A hidden 720x180 window rejected protection; its dimensions match the global-dictation overlay in the installed app. Overlay exclusion remains unverified.
- Windows accepted the native tray icon's NIS_HIDDEN request. No visual inspection confirmed the final tray state. The controller's own full-mode tray icon is disabled by default.
- The user confirmed native Ctrl+Shift+D dictation working after a full ChatGPT restart.
- No specific third-party screen-sharing application was tested.

The port also corrects x64 INPUT union sizing and the GetCurrentThreadId DLL import. State/log/recovery storage is separated from Classic.

--stop is scoped to full-mode processes named ChatGPTController installed under %LOCALAPPDATA%\ChatGPTCurrentController\app. It uses the existing emergency recovery handler, without force termination. As of 21 October 2026, shutdown drains protection scans, restores the window and shell state, then resets capture affinity to WDA_NONE on current-package windows. The earlier test observations above predate this shutdown change.


## Manual mode and ordinary shutdown, 21 October 2026

The Windows Startup shortcut was removed on the configured machine; matching Run entries and scheduled tasks were not found. The controller now starts through the desktop enable shortcut only.

The installed full-mode build was exercised twice. Before the first shutdown the target was hidden, topmost, click-through and at alpha 242. Shutdown restored a visible window with topmost/click-through off, alpha 255 and affinity 0. All 21 enumerated current-app windows reported WDA_NONE. A second enable/show/disable cycle reapplied 0x11 while enabled, returned shutdown exit code 0, then left all 21 windows at WDA_NONE after waiting for delayed callbacks. No controller process remained running. The native dictation binding was preserved.
