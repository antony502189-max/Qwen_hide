# ChatGPT Controller

Windows x64 companion for the current Microsoft Store ChatGPT application packaged as `OpenAI.Codex`. This ports the Classic controller's features to the current application. The repository keeps its historical name, Qwen_hide.

The controller checks the executable path under `WindowsApps\OpenAI.Codex_<version>_x64__2p2nqsd0c76g0\app\ChatGPT.exe`; it does not target Classic or unrelated ChatGPT.exe processes.

## Build and install

Requires Windows x64 and .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The self-contained output is `dist-single\ChatGPTDesktopController.exe`. Install it with the filename expected by the shutdown command:

```powershell
$installDir = Join-Path $env:LOCALAPPDATA 'ChatGPTCurrentController\app\2026.10.20.3'
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item .\dist-single\ChatGPTDesktopController.exe (Join-Path $installDir 'ChatGPTController.exe')
```

Use a new version directory when updating a running installation, then update your shortcuts.

- Enable shortcut: installed `ChatGPTController.exe`, no arguments.
- Disable shortcut: the same executable with `--stop`. It invokes emergency recovery and waits for graceful exit, without force-killing. It targets full-mode controllers installed under `%LOCALAPPDATA%\ChatGPTCurrentController\app` only. Exit code 2 means shutdown was not confirmed.
- Emergency shutdown: **Ctrl+Alt+Esc**.
- Diagnostics: **Ctrl+Alt+D**, or `--diagnostics` when starting a fresh instance.

Default full mode starts without a visible controller window or controller tray icon. It suppresses the target taskbar button and attempts to hide ChatGPT's native tray icon. Settings, logs and recovery data are separate from Classic under `%LOCALAPPDATA%\ChatGPTCurrentController`.

## Hotkeys

| Keys | Action |
|---|---|
| Ctrl+Alt+Q | Hide/show ChatGPT; keep controller running |
| Ctrl+Alt+X | Toggle click-through |
| Ctrl+Alt+T | Toggle always-on-top |
| Ctrl+Alt+Up / Down | Adjust opacity |
| F6 | Capture monitor to clipboard |
| Ctrl+Alt+V | Show, protect and focus ChatGPT; paste image |
| Ctrl+Alt+D | Diagnostics |
| Ctrl+Shift+R | Legacy accessible voice-button action |
| Ctrl+Alt+Esc | Restore window state and exit |
| Right Ctrl | Optional audio mix; requires configured virtual audio devices |

**Ctrl+Shift+D dictation belongs to ChatGPT itself**, independently of the controller. Merge this entry into the array in `%USERPROFILE%\.codex\keybindings.json`, preserving existing bindings, then fully restart ChatGPT:

```json
{ "command": "globalDictationSingleTap", "key": "Ctrl+Shift+D" }
```

This command was confirmed on the tested application version; later versions may change it.

## Verification and limitations

44 automated tests passed. Main-window exclusion was observed with PrintWindow, GDI capture and Windows Graphics Capture on the tested machine. The latest DXGI control was inconclusive. No specific third-party screen-sharing application was tested.

A hidden 720x180 window consistent with the global-dictation overlay rejected protection. **Dictation overlay exclusion is not verified.** Native tray hiding was accepted by Windows, but not visually confirmed. See [verification notes](CURRENT_PORT_VERIFICATION.md) for coverage.

The optional `--privacy-only` mode is an older guard-only mode with a visible tray icon, no full hotkeys and no `--stop` receiver; exit through its tray menu. Desktop shortcuts use full mode.

[Русское руководство](GUIDE_RU.md) · [English guide](GUIDE_EN.md)

Existing capture/audit PowerShell scripts still target **Classic**. They and `RELEASE_AUDIT.txt` / `MANUAL_TEST_CHECKLIST_EN.md` are historical tools and records, not validation of this port.
