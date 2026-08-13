# Qwen Desktop Controller — User Guide

## 1. What this project is

Qwen Desktop Controller is a Windows companion process for the **Qwen Desktop application that is already installed on your PC**.

It does not host `qwen.ai` in WebView2 and it does not create a second Qwen session. The normal Qwen Desktop window remains the window you use for chats, model selection, files, images, code and Qwen's built-in voice feature.

The controller only adds Windows-level conveniences around that existing window.

## 2. Current feature set

- automatically discovers a running Qwen Desktop process/window;
- can launch an existing `Qwen.exe` when its path is known or auto-detected;
- applies adjustable whole-window opacity to the real Qwen window;
- toggles TopMost on the real Qwen window;
- toggles reversible mouse click-through on the real Qwen window;
- hides/shows Qwen without terminating it;
- captures the last non-Qwen work window directly to the Clipboard with `F6`;
- captures the current monitor to the Clipboard with `Shift+F6`;
- pastes the current Clipboard into the real Qwen app with `Ctrl+Alt+V`;
- captures your physical microphone in shared mode;
- captures Windows playback using WASAPI loopback;
- mixes microphone + system audio in memory;
- can send the mixed signal to a recognized virtual-cable render endpoint only;
- never intentionally changes Windows default or communications audio endpoints;
- toggles Qwen's **existing** voice recording with `Ctrl+Shift+R` through calibrated native input;
- restores the Qwen window's original extended styles / TopMost state when the controller exits;
- lives in the Windows system tray during normal use.

## 3. Capture privacy: verified host, unverified share applications

The native Qwen top-level window belongs to Qwen, so the controller never calls `SetWindowDisplayAffinity` on that foreign HWND. **Toggle Privacy Host** creates a normal controller-owned top-level HWND, applies `WDA_EXCLUDEFROMCAPTURE` to that host, reads it back with `GetWindowDisplayAffinity`, and only then reparents the real installed Qwen HWND as a child.

Before Qwen changes parent, the recovery journal durably records and verifies its parent, style/ex-style, `WINDOWPLACEMENT`, visibility, minimized/maximized state, TopMost state, DPI, and DPI-awareness context. The host must have both matching non-zero window DPI and an equivalent awareness context, and DWM composition must be enabled. Any failed journal, DWM/DPI check, affinity verification, style change or `SetParent` check restores Qwen and leaves privacy disabled. `Ctrl+Alt+Esc`, normal shutdown, unhandled-exception cleanup and next-start stale-journal recovery use the same restoration data.

`Privacy host ON` means the host affinity was genuinely set and read back. It does **not** mean every capture product honors it. The controller cannot certify full-monitor sharing in Teams, Zoom, Google Meet, or Yandex Telemost without observing each product's shared output. Treat an application as unsupported until it passes the manual matrix in `MANUAL_TEST_CHECKLIST_EN.md`.

The optional **Validate GDI Capture** action samples four bounded patches while the active host is visible and briefly hidden. It stores only aggregate pixel statistics, not images. Its result is explicitly limited to legacy GDI screen copying. A matching visible/hidden sample is always `Inconclusive`, because it cannot prove host absence; `RedactedPlaceholder` means content was redacted but the host was not proven absent; `Exposed` means host content was captured. None establishes behavior for Desktop Duplication, Windows Graphics Capture, or conferencing software.

**Validate PrintWindow** samples only a 24x24 grid from an in-memory `PrintWindow` render of the controller-owned host. An `Exposed` result means that direct window-capture API rendered non-uniform host content. A blank or uniform render is only `Inconclusive`, and neither result says anything about full-monitor sharing. No image, chat content, or screenshot file is retained.

The controller's **Validate Native Capture APIs** action invokes the packaged `privacy-capture-probe.exe` and `privacy-wgc-capture-probe.exe` for **Desktop Duplication** and full-monitor **Windows Graphics Capture**. It records only their bounded aggregate-pixel result lines in Diagnostics and briefly hides/restores the host; no screenshots or files are created. The helpers may also be run manually with `0xHOSTHWND` from Diagnostics. `RedactedPlaceholder` protects Qwen content but is still not proof that the host is absent from a full-monitor share; a matching sample is reported as `Inconclusive`.

## 4. Requirements

- Windows 10 or Windows 11 x64;
- Qwen Desktop already installed;
- .NET 8 SDK for building from source;
- no WebView2 requirement for this controller;
- optional virtual audio cable only if you want Qwen to receive microphone + system audio as one input.

## 5. Initial setup

Open PowerShell in the repository:

```powershell
cd E:\qwen_hide
.\scripts\setup.ps1
```

Then build:

```powershell
.\scripts\build.ps1
```

The published executable is expected at:

```text
dist\QwenDesktopController.exe
```

Run it:

```powershell
.\dist\QwenDesktopController.exe
```

## 6. Attaching to your existing Qwen

The controller first searches running processes whose executable/process identity looks like Qwen and then finds the visible top-level Qwen window.

Recommended first test:

1. Open Qwen Desktop normally.
2. Confirm your normal chats/account/model are visible.
3. Launch `QwenDesktopController.exe`.
4. Open the controller from the tray if it hides automatically.
5. The status should say **Attached to the installed Qwen Desktop**.
6. Diagnostics should show Qwen's PID, HWND, window class and executable path.

If Qwen is not detected, open **Settings** and browse to the installed `Qwen.exe` manually. The controller can store that executable path for subsequent launches.

## 7. Window hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+Q` | Hide/show the real Qwen window |
| `Ctrl+Alt+X` | Toggle click-through |
| `Ctrl+Alt+T` | Toggle always-on-top |
| `Ctrl+Alt+Up` | Increase Qwen opacity by 5% |
| `Ctrl+Alt+Down` | Decrease Qwen opacity by 5% |
| `Ctrl+Alt+V` | Paste Clipboard contents into the real Qwen app |
| `Ctrl+Alt+D` | Open diagnostics |
| `F6` | Capture the last active non-Qwen work window to Clipboard |
| `Shift+F6` | Capture the monitor under the mouse cursor to Clipboard |
| `Ctrl+Shift+R` | Toggle Qwen voice recording ON/OFF |
| Hold `Right Ctrl` | Enable the configured Qwen-only audio mix |

## 8. Transparency

Default opacity is 85%.

Use:

- `Ctrl+Alt+Up` to make Qwen more opaque;
- `Ctrl+Alt+Down` to make it more transparent.

Allowed range: 35%–100%.

The controller modifies the real Qwen top-level window style and uses layered-window alpha. The change is reversible. If your particular Qwen build renders incorrectly when layered transparency is applied, restore opacity to 100% and report the diagnostics (window class + executable version/path).

## 9. Click-through

Press `Ctrl+Alt+X`.

When click-through is ON:

- Qwen remains visible;
- mouse clicks go to the application underneath it;
- you can read an answer while clicking/typing in VS Code or another app below.

Press `Ctrl+Alt+X` again to return Qwen to normal interaction.

The hotkey is global so you can recover even when Qwen no longer accepts mouse input.

## 10. Screenshot → Clipboard

### Active work window

Press:

`F6`

The controller uses the last active window that was neither Qwen nor the controller itself. It first attempts `PrintWindow`, which can capture the target window independently of visual occlusion. If that fails it falls back to screen-copy capture.

The result goes directly to the Windows Clipboard. No PNG/JPG is intentionally saved to disk.

Then focus Qwen and press:

`Ctrl+V`

### Current monitor

Press:

`Shift+F6`

For this controller-owned screenshot function, Qwen is briefly hidden before the screen copy and shown again afterward so the Qwen window is not included in the clipboard screenshot.

## 11. Clipboard → Qwen

Press:

`Ctrl+Alt+V`

The controller activates the existing Qwen window and sends a normal `Ctrl+V` after the global hotkey modifiers have had time to release.

It does not alter text and does not prepend an instruction.

## 12. Audio architecture

The intended topology is:

```text
Physical microphone ─────┐
                         ├── Qwen mix ──> optional virtual cable ──> Qwen input
Windows playback ────────┘

Physical microphone ───────────────────────────────────────> Teams / Zoom normally
```

The controller does **not** intentionally change:

- Windows Default Input Device;
- Windows Default Communications Input Device;
- Windows Default Output Device;
- Windows Default Communications Output Device.

The physical microphone is opened in shared mode, so conferencing software should still be able to use it normally.

## 13. Configuring microphone + system audio for Qwen only

The native controller cannot safely force another application to use a different input device without relying on application-specific or Windows per-app routing.

The supported safe path is:

1. Install a trusted signed virtual audio cable if you need the mixed signal.
2. In Controller **Settings**, choose your normal physical microphone.
3. Choose your normal Windows playback device as the loopback source.
4. Choose the **render/input side of the virtual cable** as `Virtual mix output`.
5. Do **not** make that cable your global Windows default microphone.
6. Configure **Qwen only** to use the paired virtual-cable capture endpoint if Qwen exposes an input-device selector.
7. If Qwen does not expose one, open Windows per-app audio settings from the controller and use per-app input routing where your Windows build supports it.

The controller rejects a virtual-mix output if it is the current Windows default output or if its device name does not look like a virtual/cable endpoint. This prevents accidentally playing the mixed signal into your speakers and creating feedback.

## 14. Right Ctrl audio behavior

If `Right Ctrl audio` is enabled in Settings:

- Right Ctrl DOWN starts shared-mode microphone + loopback capture and the in-process mixer.
- While held, the mix is rendered only to the configured recognized virtual endpoint.
- Right Ctrl UP stops the mixer.

Right Ctrl never toggles Qwen voice recording. `Ctrl+Shift+R` is the separate dedicated voice toggle. When a valid saved calibration exists, it deliberately skips the known-empty UI Automation discovery and invokes only the real Qwen window through the calibrated fallback after visibility, geometry, ownership and child-window checks.

No fake Qwen voice UI is created. If the calibrated fallback cannot be verified, it fails safely and use Qwen's normal microphone button manually.

## 15. How to verify that the controller did not break your microphone

Run this test before using the audio feature in an important call:

1. Open Windows Sound settings and note your default input device.
2. Open Teams/Zoom/Telemost and select your normal physical microphone.
3. Confirm your voice is detected there.
4. Launch Qwen Desktop Controller.
5. Open Controller Diagnostics and note `Default input before` and `Default communications input before`.
6. Hold Right Ctrl for several seconds with the audio mix configured.
7. Confirm Teams/Zoom still receives your physical microphone.
8. Release Right Ctrl.
9. Exit the controller from its tray menu.
10. Re-check Windows Sound settings and Teams/Zoom.

Expected result: your normal microphone is unchanged and still works.

If the default device changed, stop using the audio feature and report the diagnostics. The controller itself contains no code intended to set a default endpoint.

## 16. Virtual audio setup helper

The repository contains:

```powershell
.\scripts\setup-virtual-audio.ps1
```

This helper does not silently install or reconfigure drivers. It can open a vendor installer with UAC when you explicitly provide one. Driver installation may require administrator approval and a Windows restart.

## 17. Diagnostics

Press `Ctrl+Alt+D` or open **Diagnostics** from the controller.

Useful fields include:

- native Qwen attached;
- executable path;
- PID;
- HWND;
- window class;
- opacity;
- TopMost;
- click-through;
- microphone status;
- loopback status;
- mixer status;
- virtual output status;
- Qwen voice UI Automation state;
- matched voice-control label;
- audio default-device guard status;
- capture-privacy limitation.

## 18. Logs

Technical logs are stored under:

```text
%LOCALAPPDATA%\QwenDesktopController\logs\app.log
```

The controller should not log chat contents, Clipboard contents, screenshots or audio content.

## 19. Troubleshooting

### Controller says Qwen is not attached

Open Qwen Desktop normally, then click **Attach / Open Qwen**. If necessary select the exact `Qwen.exe` path in Settings.

### Transparency makes Qwen render incorrectly

Set opacity back to 100%. Send the window class and Qwen executable path/version from Diagnostics before changing the rendering approach.

### Click-through is on and Qwen cannot be clicked

Press `Ctrl+Alt+X`. It is global and is specifically intended as the recovery control.

### F6 captures the wrong window

Activate the work window you want first, then press F6. The controller tracks the most recent non-Qwen foreground top-level window.

### Mixed audio is unavailable

Open Settings and verify:

- physical microphone selected;
- playback/loopback endpoint selected;
- recognized virtual cable render endpoint selected.

The controller intentionally refuses to render the mix to a normal physical speaker/headset endpoint.

### Qwen voice does not start with Ctrl+Shift+R

Open Diagnostics. If the calibrated click cannot be verified, start Qwen Voice manually. The audio mixer remains independent of voice recording.

### Capture privacy is not a full-share pass

The controller never fakes affinity on Qwen's foreign HWND: privacy mode applies and verifies it only on the controller-owned host. A verified host is still not proof that a particular capture API or conferencing product excludes it. On the target machine GDI has shown **Exposed** and inconclusive samples; direct `PrintWindow` returned a uniform, **Inconclusive** host render; Desktop Duplication and Windows Graphics Capture have shown **RedactedPlaceholder** samples. Validate each full-monitor share application separately.

## 20. Building from source

```powershell
.\scripts\build.ps1
```

The script performs restore, Release build, tests and self-contained `win-x64` publish.

Expected executable:

```text
dist\QwenDesktopController.exe
```

The GitHub repository also contains a Windows CI workflow that builds, tests and publishes a downloadable workflow artifact.
