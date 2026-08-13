# Manual acceptance checklist — Qwen Desktop Controller

Run these tests on the target Windows machine with the actual installed Qwen Desktop application.

## A. Native Qwen identity

- [ ] Start Qwen Desktop normally before the controller.
- [ ] Existing account is already logged in.
- [ ] Existing chat history is visible.
- [ ] Existing model selection is visible.
- [ ] Start `QwenDesktopController.exe`.
- [ ] No second qwen.ai/WebView Qwen window appears.
- [ ] Diagnostics reports the actual Qwen PID/HWND/executable path/window class.
- [ ] Starting a second controller instance is refused and does not attach twice.
- [ ] Closing the controller does not close or damage Qwen.

## B. Window controls

- [ ] Qwen can still be moved normally with its own title bar.
- [ ] Qwen can still be resized normally.
- [ ] `Ctrl+Alt+Up` increases opacity.
- [ ] `Ctrl+Alt+Down` decreases opacity.
- [ ] Qwen remains usable after opacity changes.
- [ ] 100% opacity removes controller-added layered alpha when Qwen did not originally use it.
- [ ] `Ctrl+Alt+T` toggles TopMost.
- [ ] `Ctrl+Alt+Q` hides and restores the same Qwen process/session.
- [ ] Minimize/maximize Qwen and repeat the controls; its normal window state remains usable.

## C. Click-through and emergency recovery

- [ ] Put Qwen over VS Code/Notepad.
- [ ] Press `Ctrl+Alt+X`.
- [ ] Qwen stays visible.
- [ ] Mouse clicks reach the application underneath Qwen.
- [ ] Press `Ctrl+Alt+X` again.
- [ ] Qwen receives normal mouse input again.
- [ ] Qwen scrolling, typing, file dialogs and copy/paste still work afterward.
- [ ] Turn click-through ON again, then press `Ctrl+Alt+Esc`.
- [ ] Controller exits and restores the original Qwen window style/interaction.

## D. Screenshot → Clipboard

- [ ] Activate a non-Qwen work window.
- [ ] Press `F6`.
- [ ] Open Paint and press `Ctrl+V`.
- [ ] The intended work window is present in the clipboard image.
- [ ] Repeat with a Chromium/Electron app; a black `PrintWindow` result must fall back to screen-copy capture.
- [ ] Qwen is not visibly composited over the fallback image.
- [ ] Qwen returns to the same visible/minimized/maximized state after capture.
- [ ] No screenshot PNG/JPG was created by the controller.
- [ ] Press `Shift+F6` and verify the monitor screenshot appears in the Clipboard.
- [ ] Repeatedly press F6 while another app is also using the Clipboard; the retry logic must not crash the controller.

## E. Clipboard → existing Qwen

- [ ] Copy text in another app.
- [ ] Press `Ctrl+Alt+V`.
- [ ] Existing Qwen becomes active.
- [ ] Exact copied text is pasted into Qwen.
- [ ] No extra prompt/instruction was added.
- [ ] Repeat with an image if Qwen supports normal image paste on this build.

## F. Global hotkeys

- [ ] `Ctrl+Alt+D` Diagnostics reports `All global hotkeys registered`.
- [ ] Right Ctrl hook reports READY.
- [ ] If a hotkey is occupied by another application, Diagnostics reports the exact failed hotkey instead of silently pretending it works.
- [ ] Holding a registered hotkey does not repeatedly retrigger it because MOD_NOREPEAT is used.
- [ ] Right Ctrl key repeat does not start multiple audio sessions.

## G. Audio isolation — CRITICAL

Before starting:

- [ ] Note the Windows Default Input Device.
- [ ] Note the Windows Default Communications Input Device.
- [ ] Note the Windows Default Output Device.
- [ ] Start Teams/Zoom/Telemost and verify it uses the physical microphone.
- [ ] Confirm your voice reaches the call application normally.

Controller test:

- [ ] Open Controller Settings.
- [ ] Select the physical microphone.
- [ ] Select the normal playback endpoint for WASAPI loopback.
- [ ] If using a virtual cable, select only its recognized render endpoint as `Virtual mix output`.
- [ ] Verify a physical speaker/headset cannot be accepted as the virtual mix destination.
- [ ] Verify the Windows default/communications output cannot be selected as a mix destination.
- [ ] Verify the virtual cable has NOT become Windows Default Input.
- [ ] Configure only Qwen to use the paired virtual capture endpoint where supported.
- [ ] Hold Right Ctrl.
- [ ] Diagnostics shows microphone capture READY.
- [ ] Diagnostics shows system loopback READY.
- [ ] Diagnostics shows the recognized virtual output READY.
- [ ] `Mic bytes`, `Loopback bytes` and `Mixed frames` increase while the corresponding signals exist.
- [ ] Qwen receives your voice through the configured Qwen-only path.
- [ ] Qwen receives system/call audio through the configured Qwen-only path.
- [ ] Teams/Zoom still receives your physical microphone.
- [ ] System playback is NOT echoed back into the Teams/Zoom microphone.
- [ ] Release Right Ctrl and verify the mix stops cleanly.
- [ ] Repeat Right Ctrl down/up at least 20 times; no stale callbacks, crashes or duplicate sessions occur.

After closing controller:

- [ ] Teams/Zoom microphone still works.
- [ ] Windows Default Input Device is unchanged.
- [ ] Windows Default Communications Input Device is unchanged.
- [ ] Windows output devices are unchanged.

Any failure in this section is an acceptance blocker for audio use.

## H. Qwen voice-button automation

- [ ] Open Diagnostics with Qwen visible.
- [ ] Check `Qwen voice automation` status.
- [ ] If a voice-like control is detected, note its matched accessible label.
- [ ] With the virtual mix ready, hold Right Ctrl.
- [ ] Qwen's existing voice input starts if the accessibility control is invokable.
- [ ] Release Right Ctrl.
- [ ] Qwen's existing voice input stops/toggles back if supported.
- [ ] If automation is unavailable, disabling it in Settings leaves manual Qwen voice usage normal.

## I. Qwen restart/re-attach

- [ ] Start controller and verify it is attached.
- [ ] Exit only Qwen Desktop while leaving the controller running.
- [ ] Controller detects the stale HWND without crashing.
- [ ] Start Qwen Desktop again.
- [ ] Controller reacquires the new PID/HWND automatically.
- [ ] Opacity/TopMost settings are applied to the new Qwen window.
- [ ] Existing Qwen data/session behavior remains Qwen's own behavior and is not recreated by the controller.

## J. Crash recovery journal — CRITICAL

This test deliberately terminates the controller to simulate a crash.

- [ ] Start Qwen and controller.
- [ ] Turn on transparency, TopMost and click-through.
- [ ] Confirm `%LOCALAPPDATA%\QwenDesktopController\window-recovery.json` exists while Qwen is being controlled.
- [ ] Kill **only** `QwenDesktopController.exe` from Task Manager. Do not kill Qwen.
- [ ] Qwen may temporarily retain the modified native window style because graceful cleanup was bypassed.
- [ ] Start `QwenDesktopController.exe` again.
- [ ] Before re-attaching, the controller restores the previous Qwen native style from the journal.
- [ ] The journal is deleted only after restoration is verified.
- [ ] Qwen is not left permanently click-through or transparent.
- [ ] Repeat once while Qwen is closed before controller restart; stale journal is discarded without touching any unrelated HWND/PID.

## K. Normal restore behavior

- [ ] Note Qwen opacity/topmost/click behavior before controller test.
- [ ] Turn on controller transparency/click-through/topmost changes.
- [ ] Exit the controller from the tray using **Exit controller (restore Qwen)**.
- [ ] Qwen remains running.
- [ ] Qwen mouse interaction is normal.
- [ ] Controller-specific click-through is gone.
- [ ] Original Qwen extended style/TopMost/visibility is restored.
- [ ] No recovery journal remains after verified successful restore.

## L. Privacy host and full-monitor capture matrix

- [ ] With Qwen visible and restored (not minimized), click **Toggle Privacy Host**.
- [ ] Diagnostics shows a non-zero Privacy host HWND, Qwen child HWND, original/current parent, matching non-zero host/Qwen DPI, `WDA requested: 0x11`, and `WDA verified: 0x11`.
- [ ] Click **Validate GDI Capture** and record its exact result. `LikelyExcluded` applies only to that legacy GDI screen-copy probe; `Exposed`, `RedactedPlaceholder`, or `Inconclusive` is not a strict capture-exclusion pass.
- [x] Target-machine result (Windows 10 19045 x64, Qwen 1.0.3): distributed GDI screen-copy probe returned **Exposed** (latest run: difference 43.5; visible variance 1973.3). This pipeline captures real Qwen content and is unsupported.
- [x] Target-machine result: Desktop Duplication returned **RedactedPlaceholder** (difference 33.7; visible variance 0.0; hidden variance 2539.8). Qwen content was not observed, but the captured surface was not proven absent; do not call this strict `WDA_EXCLUDEFROMCAPTURE` PASS.
- [ ] Local monitor: Qwen remains visible and accepts mouse, keyboard, clipboard, resize, restore and maximize operations.
- [ ] Click **Toggle Privacy Host** again and confirm the original parent, style, ex-style, placement, visibility and TopMost state are restored. Then test `Ctrl+Alt+Esc` while host mode is ON.
- [ ] Microsoft Teams: share the **entire monitor**; observe the remote/shared preview. Record PASS only when Qwen is locally visible but absent from shared output.
- [ ] Zoom: share the **entire monitor**; observe the remote/shared preview and record PASS/UNSUPPORTED.
- [ ] Google Meet: share the **entire monitor**; observe the remote/shared preview and record PASS/UNSUPPORTED.
- [ ] Yandex Telemost: share the **entire monitor**; observe the remote/shared preview and record PASS/UNSUPPORTED.
- [ ] Record capture APIs separately: GDI/PrintWindow results do not prove Desktop Duplication, Windows Graphics Capture, or a conferencing application's behavior.
- [ ] The controller does not inject a DLL into Qwen, patch Qwen binaries, or claim an application is protected without the observed shared output.

## M. Build/package

- [ ] GitHub Actions architecture guard passes and confirms no WebView2/Qwen web wrapper was reintroduced.
- [ ] `scripts\setup.ps1` succeeds.
- [ ] `scripts\build.ps1` succeeds.
- [ ] Automated tests pass.
- [ ] CI verifies `dist\QwenDesktopController.exe` exists and emits its SHA-256.
- [ ] The self-contained win-x64 artifact launches on the target Windows PC without Visual Studio.
