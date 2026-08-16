# ChatGPT Classic Controller — guide

1. Open the normal installed ChatGPT Classic application.
2. Run `ChatGPTDesktopController.exe`.
3. Use `Attach / refresh` if it was opened after the controller.

Hotkeys: `Ctrl+Alt+Q` hide/show; `Ctrl+Alt+X` click-through; `Ctrl+Alt+T` TopMost; `Ctrl+Alt+Up/Down` opacity (35–100%); `F6` capture the active work window to the native Windows Clipboard; `Ctrl+Alt+V` restore/activate ChatGPT, focus the UI Automation composer, then send Ctrl+V; `Ctrl+Alt+D` diagnostics; `Ctrl+Alt+Esc` restore and quit.

Paste intentionally fails with diagnostics if the ChatGPT composer is not exposed reliably through UI Automation. It never uses a coordinate click fallback. Hold no modifiers while it completes: the controller explicitly waits for Ctrl/Alt/V to be released before sending the paste.

Voice and mixed audio are fail-closed. The current implementation does not guess an undocumented ChatGPT voice shortcut and does not alter Windows defaults or conference-app microphone selections. Run `scripts\probe-chatgpt-classic.ps1` while ChatGPT is open to record the available UI Automation controls before enabling a target-specific native shortcut.

The recovery journal is only written immediately before the first window mutation. On normal exit, emergency exit, or next startup after a crash it restores original visibility, styles, topmost state, alpha/layering, and window placement.

Screen-share exclusion is not supported or claimed. Windows display affinity does not guarantee exclusion from full-desktop conferencing shares.
