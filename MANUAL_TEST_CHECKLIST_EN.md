# Manual acceptance checklist

## A — Qwen
- Launch the release, log in, close and reopen; verify the Qwen session remains authenticated.

## B — Window
- Drag and resize the window, change opacity, toggle topmost, hide/show with Ctrl+Alt+Q, then relaunch to verify restoration.

## C — Click-through
- Place the overlay above Notepad, press Ctrl+Alt+X, click Notepad through it, then press Ctrl+Alt+X again.

## D — Screenshot
- Press F6, paste into Paint, verify the image exists, and verify no PNG/screenshot archive exists under the app data directory.

## E — Capture protection
- Toggle Ctrl+Alt+P; ensure the status is ON only when Diagnostics reports ON. Test with a Windows capture pipeline that documents support for display affinity.

## F — Audio isolation
1. Record default input and communications IDs from Diagnostics before capture.
2. Start Zoom/Teams with its physical microphone.
3. If a trusted virtual cable is installed, select its render side in Settings and the paired capture side in Qwen. Hold Right Ctrl; verify Diagnostics reports a READY virtual-cable route.
4. Confirm Zoom/Teams continues receiving only its physical microphone.
5. Exit and verify all default endpoint IDs did not change. Without a separately installed virtual endpoint, Diagnostics must clearly report that injection is unavailable.
