# Qwen Work Overlay

Windows desktop shell for the real [qwen.ai](https://qwen.ai/) website. It stores the embedded browser profile under `%LOCALAPPDATA%\QwenWorkOverlay\WebViewProfile`, so Qwen sessions are retained by the browser—not by this app.

Features: borderless movable/resizable shell, adjustable whole-window opacity, always-on-top, click-through, capture-exclusion status, global hotkeys, clipboard screenshots, diagnostics, and safe shared-mode microphone / WASAPI loopback capture. It never changes Windows default audio devices, never uses exclusive audio mode, and never plays a mixed stream through speakers.

## Quick start

1. Run `scripts\setup.ps1`.
2. Run `scripts\build.ps1`.
3. Launch `dist\QwenWorkOverlay.exe` and sign in to Qwen normally.

See [GUIDE_EN.md](GUIDE_EN.md) and [MANUAL_TEST_CHECKLIST_EN.md](MANUAL_TEST_CHECKLIST_EN.md).
