<div align="center">

# ChatGPT Classic Controller

### Privacy-first desktop companion for the Microsoft Store ChatGPT Classic app on Windows

Control the real ChatGPT window, keep it available locally, and exclude it from supported Windows capture paths used by screenshots and screen sharing.

<p>
  <img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078D4?logo=windows11&logoColor=white">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="C#" src="https://img.shields.io/badge/C%23-WPF-239120?logo=csharp&logoColor=white">
  <img alt="Privacy" src="https://img.shields.io/badge/Capture_Privacy-WDA__EXCLUDEFROMCAPTURE-178A4B">
  <img alt="Status" src="https://img.shields.io/badge/Status-Runtime_Validated-2C6BED">
</p>

[Русское руководство](GUIDE_RU.md) · [English guide](GUIDE_EN.md) · [Manual test checklist](MANUAL_TEST_CHECKLIST_EN.md) · [Release audit](RELEASE_AUDIT.txt)

</div>

---

## What it is

**ChatGPT Classic Controller** is a native Windows companion for the installed Microsoft Store **ChatGPT Classic** application.

It attaches to the real desktop app window — it is **not** a browser wrapper and does **not** read conversations, inspect account data, or access ChatGPT session credentials.

The controller validates both the target process and its executable location under the `OpenAI.ChatGPT-Desktop` Microsoft Store package before attaching, so it does not attach to unrelated `ChatGPT.exe` processes.

### Core idea

```text
You see ChatGPT locally
        │
        ├── window controls / hotkeys
        ├── privacy verification
        ├── taskbar suppression
        └── screenshot + screen-share protection

Supported Windows capture path
        │
        └── ChatGPT content excluded
```

---

## Highlights

<table>
<tr>
<td width="50%" valign="top">

### 🛡 Capture privacy

- Applies `WDA_EXCLUDEFROMCAPTURE` (`0x11`)
- Verifies protection before sensitive transitions
- Re-checks privacy after window state changes
- Fail-closed behavior when visible privacy cannot be verified
- F6 capture no longer hides/shows ChatGPT
- Protects against the transient flash previously observed during screen sharing

</td>
<td width="50%" valign="top">

### 🪟 Window control

- Hide / show ChatGPT instantly
- Always-on-top mode
- Click-through mode
- Adjustable opacity
- Preserves original minimized / maximized / normal state
- Suppresses the ChatGPT taskbar entry while the controller is active

</td>
</tr>
<tr>
<td width="50%" valign="top">

### ⚡ Automation

- Global hotkeys
- Screenshot active monitor to clipboard
- UI Automation based image insertion
- Voice button invocation through `InvokePattern`
- Optional audio-routing functionality with explicit device selection

</td>
<td width="50%" valign="top">

### ♻ Recovery & safety

- Single controller instance
- Recovery journal before the first window mutation
- Restores window styles, visibility, TopMost, opacity and position
- Safe exit from tray
- Emergency recovery hotkey
- Periodic target reacquisition

</td>
</tr>
</table>

---

## Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl + Alt + Q` | Hide / show ChatGPT |
| `Ctrl + Alt + X` | Toggle click-through |
| `Ctrl + Alt + T` | Toggle always-on-top |
| `Ctrl + Alt + ↑` | Increase opacity |
| `Ctrl + Alt + ↓` | Decrease opacity |
| `F6` | Capture the active monitor to the Windows clipboard without toggling ChatGPT visibility |
| `Ctrl + Alt + V` | Show ChatGPT, focus a supported composer via UI Automation and insert an image |
| `Ctrl + Alt + D` | Open diagnostics |
| `Ctrl + Shift + R` | Invoke the available voice control through UI Automation |
| `Ctrl + Alt + Esc` | Emergency restore and exit |

> Opacity is constrained to the supported controller range. Image insertion intentionally fails with diagnostics if a compatible editor cannot be found — no coordinate-based clicking is used.

---

## Privacy model

The controller uses the Windows display-affinity mechanism and expects an exact verified value of:

```text
WDA_EXCLUDEFROMCAPTURE = 0x11
```

For sensitive paths such as F6 capture and show transitions, the controller performs privacy verification before proceeding. If a visible target cannot be verified as protected, the safety path is designed to fail closed rather than continue as if privacy were healthy.

### Important limitation

This project provides **best-effort protection for supported public Windows capture mechanisms**. It is not universal DRM and does not claim protection against every possible privileged, kernel-level, driver-level, injected, or hardware capture technique.

---

## Runtime validation

The current implementation was manually validated on the project's Windows 10 x64 test configuration.

| Check | Result |
|---|---:|
| Release build | ✅ PASS |
| Compiler warnings / errors | ✅ `0 / 0` |
| Unit tests | ✅ `35 / 35` |
| Privacy transition stress test with F6 | ✅ `25 / 25` |
| Final display affinity | ✅ `0x11` |
| F6 local window flicker | ✅ Not observed |
| F6 clipboard screenshot | ✅ ChatGPT excluded |
| `Win + Shift + S` | ✅ ChatGPT excluded |
| OBS Display Capture | ✅ ChatGPT excluded |
| OBS Window Capture | ✅ ChatGPT excluded / protected output |
| Browser screen sharing | ✅ ChatGPT excluded |
| Real peer screen-share test | ✅ No transient ChatGPT frame observed |
| ChatGPT taskbar suppression | ✅ PASS |

These results describe the tested configuration, not a universal guarantee across every Windows build, GPU driver, capture product, or future ChatGPT release.

---

## Quick start

### 1. Requirements

- Windows x64
- Installed Microsoft Store **ChatGPT Classic** app
- .NET 8 SDK for building from source
- PowerShell

The repository targets `net8.0-windows` and publishes a self-contained `win-x64` build.

### 2. Build

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The build script performs:

```text
dotnet --info
    ↓
restore
    ↓
Release build
    ↓
tests
    ↓
self-contained publish
    ↓
single-file publish
    ↓
SHA-256 checksum generation
```

### 3. Output

Single-file executable:

```text
dist-single\ChatGPTDesktopController.exe
```

Checksum file:

```text
dist-single\SHA256SUMS.txt
```

### 4. Run

Recommended daily order:

```text
1. Start ChatGPTDesktopController.exe
2. Start ChatGPT Classic
3. Open diagnostics with Ctrl+Alt+D
4. Confirm privacy is protected / verified
5. Start screen sharing only after verification
```

The controller normally stays out of the way and is available from the Windows notification area.

---

## Tray menu

Right-click the controller icon in the notification area:

- **Show controller** — open the controller window
- **Diagnostics** — inspect attachment and privacy state
- **Exit safely** — restore managed window state and shut down cleanly

The tray tooltip also reflects the effective privacy state.

---

## F6 capture behavior

The privacy-safe F6 path intentionally avoids a ChatGPT hide/show transition.

```text
F6 pressed
   ↓
resolve current target
   ↓
verify visible target privacy
   ↓
require protected state
   ↓
capture active monitor
   ↓
copy image to clipboard
   ↓
post-capture privacy verification
```

Expected behavior:

- ChatGPT remains visible to you locally
- the ChatGPT window does not blink or disappear
- the screenshot is copied to the clipboard
- ChatGPT content is absent from the captured image

---

## Target discovery

The controller only considers the installed **ChatGPT Classic** Microsoft Store executable valid when the resolved path matches the expected Store package location under:

```text
\WindowsApps\OpenAI.ChatGPT-Desktop_...
```

It can discover an already-running target or locate the installed executable through Windows registration / package paths.

---

## Local data

Controller-owned runtime files are stored under:

```text
%LOCALAPPDATA%\ChatGPTDesktopController\
```

Typical contents include:

```text
settings.json
logs\
window-recovery.json
```

This data belongs to the controller itself; the application is not designed to read ChatGPT conversation history or account/session storage.

---

## Project structure

```text
Qwen_hide/
├─ .github/workflows/          # CI definition
├─ scripts/                    # build, stress and capture-privacy tools
├─ src/
│  └─ ChatGPTDesktopController/
│     ├─ MainWindow.xaml.cs
│     ├─ WindowController.cs
│     ├─ PrivacyGuardService.cs
│     ├─ PrivacyImmediateProtector.cs
│     ├─ PrivacyTransitionCoordinator.cs
│     ├─ ScreenshotService.cs
│     ├─ TaskbarVisibilityService.cs
│     ├─ TargetDiscovery.cs
│     ├─ TrayController.cs
│     └─ ...
├─ tests/                      # automated tests
├─ tools/                      # capture probe tooling
├─ GUIDE_EN.md
├─ GUIDE_RU.md
├─ MANUAL_TEST_CHECKLIST_EN.md
└─ ChatGPTDesktopController.sln
```

---

## Development notes

The project currently uses:

- **C# / .NET 8**
- **WPF** for the desktop controller
- **Windows Forms NotifyIcon** for tray integration
- **Win32 interop** for native window management and display affinity
- **UI Automation** for supported composer / voice interactions
- **NAudio 2.3.0** for optional audio functionality
- **PowerShell + native probes** for privacy and capture validation

The build is fail-fast: restore, compilation, tests, publishing and checksum creation must all succeed before the build is considered complete.

---

## Documentation

- 🇷🇺 [GUIDE_RU.md](GUIDE_RU.md) — Russian usage guide
- 🇬🇧 [GUIDE_EN.md](GUIDE_EN.md) — English usage guide
- ✅ [MANUAL_TEST_CHECKLIST_EN.md](MANUAL_TEST_CHECKLIST_EN.md) — manual acceptance checklist
- 🔎 [RELEASE_AUDIT.txt](RELEASE_AUDIT.txt) — release/audit notes

---

## Safety rule before screen sharing

> **Do not assume protection only because ChatGPT is open.**
>
> Start the controller first, confirm the protected state in Diagnostics, and only then begin an important screen-sharing session.

---

<div align="center">

**Built for controlled local use of ChatGPT Classic during screenshots, presentations and screen sharing.**

</div>
