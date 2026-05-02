# System Squire

A Windows desktop utility for system control through global hotkeys, built with C# and WPF.

Latest release download:
- [Download System Squire (latest)](https://github.com/jesseloewen/System-Squire/releases/latest/download/System_Squire.zip)

Creator website:
- I also build electronics and gaming tools at [jesseloewen.com](https://jesseloewen.com).

## Features

- Smart shutdown with a 10-second countdown
- Cancel shutdown by pressing the shutdown hotkey again
- 5-second cooldown after a cancellation
- Instant monitor blackout hotkey
- Custom global hotkeys with exact-match detection
- System tray support and start-minimized behavior
- Pushover notifications for:
  - App start and close events
  - System Squire start and exit
  - Machine inactivity with configurable resend interval

Default hotkeys:
- Shutdown: `Ctrl+Alt+F8`
- Blackout: `Ctrl+Alt+F7`

## Requirements

- Windows 10 or 11

## Quick Start

1. Download the latest release zip from the link above.
2. Extract the zip.
3. Run `System Squire.exe` from the extracted folder.

For best global hotkey reliability (including elevated apps), run as Administrator.

## Build From Source

Prerequisites:
- Visual Studio 2022+ or .NET 8 SDK
- Inno Setup 6 (only required when building the installer `.exe`)

Build and publish:

```powershell
.\build.ps1
```

Or:

```cmd
build.bat
```

Optional script flags:

```powershell
.\build.ps1 -NoRun
.\build.ps1 -Configuration Debug
.\build.ps1 -RuntimeIdentifier win-arm64
.\build.ps1 -FrameworkDependent
.\build.ps1 -BuildInstaller -NoRun
.\build.ps1 -BuildInstaller -InstallerVersion 1.2.3 -NoRun
```

Output:
- `dist\System Squire.exe`
- `installer\output\SystemSquireSetup-<version>.exe` (when `-BuildInstaller` is used)

If `ISCC.exe` is not on `PATH`, set an environment variable before building:

```powershell
$env:INNO_SETUP_COMPILER = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

## Configuration

Settings are stored in `config.json` in the application directory.

Pushover setup:
1. Open **Configure Pushover Notifications** in the main window.
2. Enable notifications and enter your App Token and User Key.
3. Choose which global events to notify on.
4. Add app names and select start/close events per app.
5. Save in both windows.

## Technical Notes

- Uses a low-level keyboard hook (`WH_KEYBOARD_LL`) for precise global hotkey matching.
- Hotkeys trigger only on exact combinations (required modifiers + key, no extra modifiers).
- Bundles `minimize-to-tray.exe` from https://github.com/danielgjackson/minimize-to-tray/ and copies it during build/publish.

## Troubleshooting

- Hotkeys not working everywhere: run as Administrator.
- Blackout not working: some display/driver combinations ignore monitor power messages.
- App will not start: ensure all files from `dist` remain together.

## License

Provided as-is for personal use.
