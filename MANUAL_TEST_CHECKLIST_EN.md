# Manual acceptance checklist — Qwen Desktop Controller

Run these tests on the target Windows machine with the actual installed Qwen Desktop application.

## A. Native Qwen identity

- [ ] Start Qwen Desktop normally before the controller.
- [ ] Existing account is already logged in.
- [ ] Existing chat history is visible.
- [ ] Existing model selection is visible.
- [ ] Start `QwenDesktopController.exe`.
- [ ] No second qwen.ai/WebView Qwen window appears.
- [ ] Diagnostics reports the actual Qwen PID/HWND/executable path.
- [ ] Closing the controller does not close or damage Qwen.

## B. Window controls

- [ ] Qwen can still be moved normally with its own title bar.
- [ ] Qwen can still be resized normally.
- [ ] `Ctrl+Alt+Up` increases opacity.
- [ ] `Ctrl+Alt+Down` decreases opacity.
- [ ] Qwen remains usable after opacity changes.
- [ ] `Ctrl+Alt+T` toggles TopMost.
- [ ] `Ctrl+Alt+Q` hides and restores the same Qwen process/session.

## C. Click-through

- [ ] Put Qwen over VS Code/Notepad.
- [ ] Press `Ctrl+Alt+X`.
- [ ] Qwen stays visible.
- [ ] Mouse clicks reach the application underneath Qwen.
- [ ] Press `Ctrl+Alt+X` again.
- [ ] Qwen receives normal mouse input again.
- [ ] Qwen scrolling, typing, file dialogs and copy/paste still work afterward.

## D. Screenshot → Clipboard

- [ ] Activate a non-Qwen work window.
- [ ] Press `F6`.
- [ ] Open Paint and press `Ctrl+V`.
- [ ] The intended work window is present in the clipboard image.
- [ ] Qwen is not visibly composited over that image when `PrintWindow` succeeds.
- [ ] No screenshot PNG/JPG was created by the controller.
- [ ] Press `Shift+F6` and verify the monitor screenshot appears in the Clipboard.

## E. Clipboard → existing Qwen

- [ ] Copy text in another app.
- [ ] Press `Ctrl+Alt+V`.
- [ ] Existing Qwen becomes active.
- [ ] Exact copied text is pasted into Qwen.
- [ ] No extra prompt/instruction was added.
- [ ] Repeat with an image if Qwen supports normal image paste on this build.

## F. Audio isolation — CRITICAL

Before starting:

- [ ] Note the Windows Default Input Device.
- [ ] Note the Windows Default Communications Input Device.
- [ ] Start Teams/Zoom/Telemost and verify it uses the physical microphone.
- [ ] Confirm your voice reaches the call application normally.

Controller test:

- [ ] Open Controller Settings.
- [ ] Select the physical microphone.
- [ ] Select the normal playback endpoint for WASAPI loopback.
- [ ] If using a virtual cable, select only its recognized render endpoint as `Virtual mix output`.
- [ ] Verify the virtual cable has NOT become Windows Default Input.
- [ ] Configure only Qwen to use the paired virtual capture endpoint where supported.
- [ ] Hold Right Ctrl.
- [ ] Controller diagnostics shows microphone capture READY/RUNNING.
- [ ] Controller diagnostics shows system loopback READY/RUNNING.
- [ ] Mixer reports running/frames.
- [ ] Qwen receives your voice through the configured Qwen-only path.
- [ ] Qwen receives system/call audio through the configured Qwen-only path.
- [ ] Teams/Zoom still receives your physical microphone.
- [ ] System playback is NOT echoed back into the Teams/Zoom microphone.
- [ ] Release Right Ctrl and verify the mix stops.

After closing controller:

- [ ] Teams/Zoom microphone still works.
- [ ] Windows Default Input Device is unchanged.
- [ ] Windows Default Communications Input Device is unchanged.
- [ ] Windows output devices are unchanged.

Any failure in this section is an acceptance blocker for audio use.

## G. Qwen voice-button automation

- [ ] Open Diagnostics with Qwen visible.
- [ ] Check `Qwen voice automation` status.
- [ ] If a voice-like control is detected, note its matched accessible label.
- [ ] With the virtual mix ready, hold Right Ctrl.
- [ ] Qwen's existing voice input starts if the accessibility control is invokable.
- [ ] Release Right Ctrl.
- [ ] Qwen's existing voice input stops/toggles back if supported.
- [ ] If automation is unavailable, disabling it in Settings leaves manual Qwen voice usage normal.

## H. Restore behavior

- [ ] Note Qwen opacity/topmost/click behavior before controller test.
- [ ] Turn on controller transparency/click-through/topmost changes.
- [ ] Exit the controller from the tray using **Exit controller**.
- [ ] Qwen remains running.
- [ ] Qwen mouse interaction is normal.
- [ ] Controller-specific click-through is gone.
- [ ] The controller restores the original Qwen extended window style as far as the OS permits.

## I. Capture privacy limitation

- [ ] Diagnostics explicitly says safe native capture privacy is unsupported for the external Qwen HWND.
- [ ] The controller does not show a false green `WDA_EXCLUDEFROMCAPTURE` status.
- [ ] Test your real conferencing app by sharing a specific work window and confirm Qwen is not part of that shared window.
- [ ] Do not treat full-desktop sharing as protected until you have verified it with your exact capture pipeline.

## J. Build/package

- [ ] `scripts\setup.ps1` succeeds.
- [ ] `scripts\build.ps1` succeeds.
- [ ] Unit tests pass.
- [ ] `dist\QwenDesktopController.exe` exists.
- [ ] The published executable launches on the target Windows PC.
