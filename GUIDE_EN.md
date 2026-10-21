# Current ChatGPT Controller guide

This port targets the Microsoft Store OpenAI.Codex package on Windows x64, rather than ChatGPT Classic. Follow [README](README.md) for build, installation, hotkeys and native dictation configuration.

Enable: installed ChatGPTController.exe without arguments. Disable: the same file with --stop. Install under %LOCALAPPDATA%\ChatGPTCurrentController\app\<version> with that filename. Shutdown invokes graceful emergency recovery; exit code 2 means exit was not confirmed.

Ctrl+Alt+Esc restores the window and exits. Ctrl+Alt+Q only hides/shows ChatGPT while the controller keeps running. Ctrl+Alt+D opens diagnostics. The controller window and its tray icon are hidden by default in full mode.

The optional --privacy-only mode has its own visible tray menu; exit that mode through its menu. The enable/disable shortcuts are for full mode.

Native Ctrl+Shift+D dictation is configured separately in ChatGPT and requires a full restart. The dictation overlay's capture exclusion is not verified. No particular third-party screen-sharing application was tested.

See [verification notes](CURRENT_PORT_VERIFICATION.md). Historical Classic capture scripts and audit files do not validate this port.
