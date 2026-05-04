# System Squire

System Squire is a Windows desktop utility for fast, keyboard-driven system control.

## Download

Install the latest official release here:

- **[Download SystemSquireSetup.exe](https://github.com/jesseloewen/System-Squire/releases/latest/download/SystemSquireSetup.exe)**

You can also browse all releases:

- [GitHub Releases](https://github.com/jesseloewen/System-Squire/releases)

## Features

- Configurable global hotkeys for shutdown and monitor blackout actions
- Smart shutdown flow with countdown, cancellation, and cooldown protection
- System tray support with start-minimized behavior
- Optional Pushover notifications for app and system activity
- Per-app notification controls for start and close events

Default hotkeys:

- Shutdown: `Ctrl+Alt+F8`
- Blackout: `Ctrl+Alt+F7`

## Requirements

- Windows 10 or Windows 11

## Installation

1. Download **SystemSquireSetup.exe** from the link above.
2. Run the installer.
3. Launch System Squire from the Start menu.

For best global hotkey reliability, run as Administrator.

## Configuration

System Squire stores settings in a local `config.json` file in the application directory.

To enable Pushover notifications:

1. Open **Configure Pushover Notifications**.
2. Enable notifications and enter your App Token and User Key.
3. Choose the events you want to receive.
4. Save your settings.

## Build From Source

Prerequisites:

- Visual Studio 2022 or newer, or .NET 8 SDK
- Inno Setup 6 (only needed to build the installer)

Build:

```powershell
.\build.ps1
```

Or:

```cmd
build.bat
```

Build script parameters:

- `-Configuration` (Debug or Release, default: Release)
	- Selects the build configuration used for publish.
- `-RuntimeIdentifier` (default: win-x64)
	- Sets the target runtime for publish.
- `-FrameworkDependent` (switch)
	- Publishes as framework-dependent instead of self-contained.
- `-BuildInstaller` (switch)
	- Builds the Inno Setup installer after publish.
- `-InstallerVersion` (string)
	- Overrides installer version metadata.
	- If omitted, version is read from the project file (`Version`, then `AssemblyVersion`, then `FileVersion`).
- `-NoVersionInInstallerName` (switch)
	- Uses `SystemSquireSetup.exe` instead of a versioned installer filename.

All parameters are also available through `build.bat` and are forwarded to `build.ps1`.

Installer build examples:

```powershell
# Default installer name includes version, e.g. SystemSquireSetup-1.1.0.exe
.\build.ps1 -BuildInstaller

# Keep legacy installer name without version suffix
.\build.ps1 -BuildInstaller -NoVersionInInstallerName
```

## Troubleshooting

- Hotkeys not working in all apps: run System Squire as Administrator.
- Blackout not working on a display: some monitor and driver combinations may not support this behavior.

## License

Provided as-is for personal use.