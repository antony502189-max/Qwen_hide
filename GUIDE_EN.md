# Qwen Work Overlay guide

## Installation and startup

Install the Microsoft Edge WebView2 Evergreen Runtime and .NET is bundled in the release. Build with `scripts\build.ps1`; the executable is `dist\QwenWorkOverlay.exe`. On first run, sign in to qwen.ai inside the embedded browser. Cookies, IndexedDB, LocalStorage, and Qwen preferences remain in `%LOCALAPPDATA%\QwenWorkOverlay\WebViewProfile`. The app never stores passwords or tokens itself.

## Window controls

Drag the thin title bar; resize from any edge/corner. The default opacity is 85%, topmost is on, and both are restored on restart. The settings button selects microphone/playback endpoints and gains, opacity, topmost, capture protection, and Right Ctrl behavior.

| Hotkey | Action |
|---|---|
| Ctrl+Alt+Up / Down | Opacity +5% / -5% (35–100%) |
| Ctrl+Alt+T | Toggle always-on-top |
| Ctrl+Alt+X | Toggle click-through; press again to restore interaction |
| Ctrl+Alt+Q | Hide/show without unloading Qwen |
| Ctrl+Alt+P | Toggle capture protection |
| F6 / Shift+F6 | Active-window / current-monitor screenshot to clipboard |
| Ctrl+Alt+V | Insert clipboard text into Qwen without sending; image uses normal paste |
| Ctrl+Alt+D | Diagnostics |
| Hold Right Ctrl | Start / stop shared microphone and loopback capture |

F6 images exist only in the Windows clipboard; the app writes no screenshot file. Press Ctrl+V in Qwen to paste manually.

## Click-through and privacy

Click-through leaves the overlay visible but lets mouse input reach the application behind it. Ctrl+Alt+X is global, so it always restores interactive mode. Capture protection calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` and only reports ON if Windows returns success. This is honored by supported Windows capture pipelines; it is not absolute protection against every recording method. Use the `TEST` button to open a protected local test window, then verify it using your capture software. Use F6, then paste into Paint, as a local clipboard check.

## Audio safety and Qwen-only mixed audio

The audio session uses shared-mode physical microphone capture and WASAPI loopback. It neither changes default input/output or communications endpoints nor uses exclusive mode, and it has no speakers output. This protects Teams/Zoom from system-audio echo. Diagnostics records the two input default endpoint IDs at launch and verifies they are unchanged at exit.

Chromium/WebView2 does not expose a standards-based way to turn a native PCM stream directly into Qwen's page `MediaStream`. The application therefore uses a safe optional fallback: it sends the 48 kHz mono mix only to a user-selected **virtual cable render endpoint**, never to speakers. Qwen continues using its normal voice UI and normal `getUserMedia`; select the paired virtual-cable microphone in that UI.

The application refuses to render a mix to the selected loopback device, the Windows default output, the communications default output, or an endpoint whose name is not recognizably virtual (Virtual, Cable, VoiceMeeter, Loopback, or BlackHole). It does not install a driver, choose Qwen's microphone, or modify any Windows default. This prevents system audio from being sent back through Teams/Zoom.

To enable the fallback, install a trusted signed virtual-audio-cable driver yourself (for example, the vendor's [VB-CABLE documentation and installer](https://vb-audio.com/Cable/)), then:

1. In Settings, select the physical microphone, the playback device to loop back, and the virtual cable's **render/input** endpoint as “Virtual mix output”.
2. In Qwen's built-in voice UI, select that cable's paired **capture/output** microphone.
3. Hold Right Ctrl while using Qwen voice. Diagnostics will report `READY` only after the virtual output starts.

`scripts\setup-virtual-audio.ps1` opens the vendor page, or accepts a locally downloaded installer path and opens it through the required UAC prompt. It never changes default devices before or after installation.

## How to verify that Qwen Work Overlay did not break your microphone

1. Record the Default Input and Default Communications Input IDs in Diagnostics.
2. Start Teams/Zoom and confirm it uses the physical microphone.
3. Run this app and hold/release Right Ctrl once; confirm Diagnostics shows either a safe virtual-cable route or a clearly stated unavailable state. Do not route any output to speakers.
4. Confirm Teams/Zoom still receives the physical microphone only.
5. Exit the app and reopen Diagnostics on the next run; compare the recorded IDs. The shutdown log in `%LOCALAPPDATA%\QwenWorkOverlay\logs\app.log` reports whether they were unchanged.

## Troubleshooting

Use Ctrl+Alt+D first. If WebView fails, install/repair WebView2 Evergreen Runtime and use `scripts\diagnose.ps1`. Logs intentionally exclude Qwen chats, clipboard contents, screenshots, audio, cookies, and credentials.
