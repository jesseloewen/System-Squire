# System Squire

A Windows desktop application for advanced system control via global hotkeys. Built with C# WPF using low-level Windows API keyboard hooks for reliable, precise hotkey detection.

## Features

- **Smart Shutdown**: Initiate system shutdown with 10-second countdown
  - Press hotkey again to cancel
  - 5-second cooldown after cancellation
- **Monitor Blackout**: Turn off monitors immediately
  - Any mouse movement or keyboard input wakes monitors
- **Configurable Hotkeys**: Record custom key combinations
  - Default: `Ctrl+Alt+F8` (Shutdown), `Ctrl+Alt+F7` (Blackout)
  - Exact match only - won't trigger on partial combinations
- **System Tray Integration**: Runs minimized to tray
- **Modern GUI**: WPF interface with dark theme

## Requirements

- Windows 10/11
- .NET 8.0 Runtime or SDK

## Building

### Prerequisites
- Visual Studio 2022 or later
- .NET 8.0 SDK

### Build Instructions

1. Open solution in Visual Studio:
   ```
   SystemSquire.sln
   ```

2. Build the solution (Ctrl+Shift+B) or from command line:
   ```powershell
   dotnet build SystemSquire.sln --configuration Release
   ```

3. Executable will be in:
  - `SystemSquire\bin\Release\net8.0-windows\System Squire.exe`

### Creating a Distributable Package

After building in Release mode, copy the application output to a folder:
```powershell
mkdir dist
copy SystemSquire\bin\Release\net8.0-windows\*.exe dist\
copy SystemSquire\bin\Release\net8.0-windows\*.dll dist\
```

## Running the Application

### From Visual Studio
- Set `SystemSquire` as startup project
- Press F5 to run

### From Built Executable
```powershell
cd SystemSquire\bin\Release\net8.0-windows
.\System Squire.exe
```

### Running as Administrator (Recommended)
For full system-wide hotkey detection (including in elevated applications):
- Right-click `System Squire.exe`
- Select "Run as administrator"

## Usage

### First Launch
1. Application starts minimized to system tray
2. Double-click tray icon to open settings window
3. Hotkeys are active immediately

### Configuring Hotkeys
1. Click "⏺ Record" button next to desired hotkey
2. Press your key combination (e.g., Ctrl+Alt+F9)
3. Click "💾 Save Configuration"
4. Hotkeys are immediately active

### Shutdown Function
- Press configured shutdown hotkey (default: `Ctrl+Alt+F8`)
- System will shutdown in 10 seconds
- Press hotkey again to cancel
- 5-second cooldown after cancellation

### Blackout Function
- Press configured blackout hotkey (default: `Ctrl+Alt+F7`)
- Monitors turn off immediately
- Move mouse or press any key to wake monitors

## Architecture

### Low-Level Keyboard Hook
The application uses Windows `SetWindowsHookEx` with `WH_KEYBOARD_LL` to intercept keyboard events at the system level. This provides:
- **Exact matching**: Only triggers on complete key combinations
- **Suppression**: Prevents hotkeys from passing through to active windows
- **Global scope**: Works across all applications

### Key Components

#### SystemSquire (Main Application)
- **KeyboardHook.cs**: Low-level keyboard hook implementation
  - Tracks pressed keys in real-time
  - Matches exact combinations (modifiers + key)
  - Suppresses matched hotkeys
- **SystemOperations.cs**: System control functions
  - Shutdown with countdown/cancellation
  - Monitor power management via `SendMessage`
- **ConfigManager.cs**: JSON-based configuration persistence
- **MainWindow.xaml**: WPF GUI with modern styling

### Configuration
Settings are stored in `config.json` in the application directory:
```json
{
  "ShutdownHotkey": "Ctrl+Alt+F8",
  "BlackoutHotkey": "Ctrl+Alt+F7",
  "DarkMode": true,
  "StartMinimized": true
}
```

## Troubleshooting

### Hotkeys Not Working
- **Run as Administrator**: Required for hotkeys to work in elevated applications
- **Conflicting Hotkeys**: Choose different combinations if conflicts exist
- **Check Configuration**: Verify settings saved correctly

### Blackout Not Working
- **Display Driver/OS Behavior**: Some systems may ignore monitor power messages

### Application Won't Start
- **.NET Runtime**: Install .NET 8.0 Runtime from Microsoft
- **Multiple Instances**: Only one instance can run at a time

## Technical Details

### Windows API Usage
- `SetWindowsHookEx`: Low-level keyboard/mouse hooks
- `SendMessage`: Monitor power management
- `shutdown.exe`: System shutdown command

### Why C# Over Python?
- **Better Keyboard Hooks**: Native Windows API integration
- **Exact Match Logic**: Fine-grained control over key combinations
- **Performance**: Lower latency for system-level hooks
- **No Dependencies**: Self-contained executables

### Hotkey Matching Logic
The keyboard hook tracks all pressed keys and only triggers callbacks when:
1. All required modifiers are pressed (Ctrl, Alt, Shift, Win)
2. The main key is pressed
3. No extra modifiers are pressed

Example: `Ctrl+Alt+F8` will NOT trigger on:
- Just `Ctrl`
- Just `Alt`
- `Ctrl+Alt`
- `Ctrl+Alt+F6` (different key)
- `Ctrl+Alt+Shift+F8` (extra modifier)

## License

This project is provided as-is for personal use.

## Version History

- **v2.0**: Complete C# rewrite with proper keyboard hooks
- **v1.0**: Python implementation (deprecated)
