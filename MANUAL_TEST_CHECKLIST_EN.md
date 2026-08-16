# Manual acceptance checklist

Run one item at a time and restore with `Ctrl+Alt+Esc` after any window-control item.

1. With ChatGPT Classic open, start the controller. Confirm diagnostics lists `ChatGPT Classic.exe` below `OpenAI.ChatGPT-Desktop` and a `Chrome_*` window class.
2. Press `Ctrl+Alt+Q` once. Observe only ChatGPT Classic hides. Press again and observe it returns.
3. Press `Ctrl+Alt+X`; attempt a click through ChatGPT to an app behind it. Press again to restore interaction.
4. Press `Ctrl+Alt+T`; activate another app and confirm ChatGPT stays above it. Press again to restore.
5. Press `F6` with an ordinary work application active. Confirm the whole monitor containing that application is copied to the Clipboard, not only the application window. Repeat once.
6. In an empty current ChatGPT conversation, press `F6`, then `Ctrl+Alt+V`. Observe whether the image attachment appears without clicking the composer. If it does not, open diagnostics and record the paste stage/method/error. The expected focus method is `UI Automation prompt-textarea`.
7. Press `Ctrl+Shift+R`. The controller must prefer the accessible dictation action (`Начало диктовки` / `Start dictation`) over full voice mode when both are exposed. Confirm speech-to-text starts immediately. Press/cancel it normally in ChatGPT before continuing.
8. Press `Ctrl+Alt+Esc`. Confirm ChatGPT’s original window state is restored and the controller exits.

Notes:
- No coordinate click fallback is used for composer or voice.
- No native ChatGPT voice keyboard shortcut was discovered in the installed build; the verified accessibility surface is used instead.
- Right Ctrl audio mixing remains disabled until a recognized non-default virtual output is configured.
