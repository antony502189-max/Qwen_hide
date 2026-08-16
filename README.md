# ChatGPT Classic Controller

Windows companion controller for the installed Microsoft Store **ChatGPT Classic** app. It controls the real application window; it does not create a browser wrapper, read conversations, or access account/session data.

Build with `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`. The self-contained executable is `dist-single\ChatGPTDesktopController.exe`.

The controller validates both process name and executable location under the `OpenAI.ChatGPT-Desktop` Store package, so it will not attach to Codex's separate `ChatGPT.exe` process.

See [GUIDE_EN.md](GUIDE_EN.md) and [GUIDE_RU.md](GUIDE_RU.md).
