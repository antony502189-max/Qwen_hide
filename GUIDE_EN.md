# ChatGPT Classic Controller — guide

1. Open the normal installed ChatGPT Classic application.
2. Run `ChatGPTDesktopController.exe`.
3. Use `Attach / refresh` if it was opened after the controller.

The notification-area icon can restore the controller, open diagnostics, or exit safely. Settings can start the controller in that area and optionally open the verified installed ChatGPT Classic executable when no target is running. Reacquisition is a low-frequency five-second check and validates the executable path each time.

Only one controller instance may run at a time. Hide/show preserves whether the target was minimized, maximized, or normal before it was hidden.

Hotkeys: `Ctrl+Alt+Q` hide/show; `Ctrl+Alt+X` click-through; `Ctrl+Alt+T` TopMost; `Ctrl+Alt+Up/Down` opacity (35–100%); `F6` capture the active work window to the native Windows Clipboard; `Ctrl+Alt+V` restore/activate ChatGPT, focus the UI Automation composer, then send Ctrl+V; `Ctrl+Alt+D` diagnostics; `Ctrl+Alt+Esc` restore and quit.

Paste intentionally fails with diagnostics if the ChatGPT composer is not exposed reliably through UI Automation. It never uses a coordinate click fallback. Hold no modifiers while it completes: the controller explicitly waits for Ctrl/Alt/V to be released before sending the paste.

Voice does not guess an undocumented ChatGPT shortcut. On the observed target, the native shortcut was not exposed, so `Ctrl+Shift+R` invokes the real accessible voice control through UI Automation `InvokePattern` (not a coordinate click). The optional Right Ctrl audio mix starts only when Settings has an explicit physical mic, loopback source, and recognized non-default virtual output. It never alters Windows defaults or conference-app microphone selections; this machine currently has no recognized virtual output, so it refuses to start.

The recovery journal is only written immediately before the first window mutation. On normal exit, emergency exit, or next startup after a crash it restores original visibility, styles, topmost state, alpha/layering, and window placement.

Screen-share exclusion is not supported or claimed. Windows display affinity does not guarantee exclusion from full-desktop conferencing shares.
