# Manual acceptance checklist

Run one item at a time and restore with `Ctrl+Alt+Esc` after any window-control item.

1. With ChatGPT Classic open, start the controller. Confirm diagnostics lists `ChatGPT Classic.exe` below `OpenAI.ChatGPT-Desktop` and a `Chrome_*` window class.
2. Press `Ctrl+Alt+Q` once. Observe only ChatGPT Classic hides. Press again and observe it returns.
3. Press `Ctrl+Alt+X`; attempt a click through ChatGPT to an app behind it. Press again to restore interaction.
4. Press `Ctrl+Alt+T`; activate another app and confirm ChatGPT stays above it. Press again to restore.
5. Press `F6` with an ordinary work window active; paste into Paint to confirm it is an image. Repeat once.
6. In an empty current ChatGPT conversation, press `F6`, then `Ctrl+Alt+V`. Observe whether the image attachment appears without clicking the composer. If it does not, open diagnostics and record the paste stage/method/error.
7. Press `Ctrl+Alt+Esc`. Confirm ChatGPT’s original window state is restored and the controller exits.

Voice has no manual acceptance item until the target probe identifies a documented/native shortcut. This avoids sending guessed shortcuts or clicking coordinates.
