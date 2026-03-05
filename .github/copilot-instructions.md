# System Squire - Workspace Instructions

## Project Overview

**System Squire** is a Windows desktop application for system control via global hotkeys:
- Smart shutdown with countdown/cancel functionality
- Monitor blackout with external process detection (Dummy.exe)
- Modern CustomTkinter GUI with configurable hotkeys

**Key Insight**: Dummy.exe serves as a detectable marker for external software when monitors are off.

## Build and Run

### Initial Setup
```powershell
pip install -r requirements.txt
python build_dummy.py  # REQUIRED before first run
```

### Run Application
```powershell
python app.py  # Standard
# Run as Administrator for full hotkey reliability
```

### After Changing dummy.py
```powershell
python build_dummy.py  # Rebuilds scripts/Dummy.exe
```

### Build Executable
```powershell
python build_app.py  # Creates System Squire.exe and Dummy.exe in dist/
```

**Note**: build_app.py automatically compiles both Dummy.exe and System Squire.exe, placing them together in the dist/ folder so they can find each other at runtime.

## Architecture

### Two-Component System
- **[app.py](app.py)**: GUI + hotkey management + system controls
  - `SystemSquire`: Core logic (shutdown, blackout, config, hotkeys)
  - `SystemSquireGUI`: CustomTkinter interface
- **[dummy.py](dummy.py)**: Detection window compiled to `scripts/Dummy.exe`
  - Opens visible window when blackout triggered
  - Uses `pynput` for **system-wide** input detection (not just window focus)
  - Closes on any mouse/keyboard input anywhere on screen

### Interaction Flow
```
Blackout hotkey → Launch Dummy.exe → Wait for window to appear (max 3s) →
Activate/bring Dummy to foreground → Release all modifier keys →
Turn off monitors → Dummy stays open (detectable) →
User input anywhere → Dummy closes → Monitors wake
```

The app uses `FindWindowW` API to verify the Dummy window actually opens before sending the monitor power-off command. It also explicitly releases all modifier keys (ctrl, alt, shift, win) to prevent them from getting stuck when the monitor turns off.

### Dual-Mode Path Resolution
The app automatically detects if it's running as a script or compiled executable:
- **Script mode** (`python app.py`): Looks for Dummy.exe in `scripts/` folder
- **Compiled mode** (`System Squire.exe`): Looks for Dummy.exe in same directory as the exe
- Detection uses `sys.frozen` attribute set by PyInstaller

## Critical Conventions

### When to Rebuild Dummy.exe
- **Always**: After any changes to [dummy.py](dummy.py)
- **Verify**: Check `scripts/Dummy.exe` exists (17-20KB typical)
- **Never commit**: Built artifacts to git (add to .gitignore if not present)

### Hotkey Implementation
- Use `keyboard.add_hotkey(hotkey, callback, suppress=True)`
- The `suppress=True` prevents hotkeys from passing through to active applications
- Register hotkeys only when monitoring is active

### Configuration
- Stored in `config.json` (auto-created, user-specific)
- Never edit while app is running (will be overwritten on save)
- Default hotkeys: `ctrl+alt+f8` (shutdown), `ctrl+alt+f7` (blackout)

### Admin Privileges
- Recommended but not strictly required
- Without admin: Hotkeys may not work in elevated applications
- With admin: Full system-wide hotkey detection

### State Management
```python
# Boolean flags for application state
self.shutdown_active = False
self.cooldown_active = False
self.hotkeys_registered = False
```

### Threading for Non-Blocking Operations
```python
# Daemon threads for cooldowns/timers
threading.Thread(target=self._cooldown_timer, daemon=True).start()
```

### Window API Usage
```python
# Monitor control
windll.user32.SendMessageTimeoutW(0xFFFF, 0x0112, SC_MONITORPOWER, MONITOR_OFF, 0x0002, 1000, None)

# Window activation
windll.user32.SetForegroundWindow(hwnd)
windll.user32.ShowWindow(hwnd, 9)  # SW_RESTORE

# Releasing modifier keys to prevent stuck keys
for key in ['ctrl', 'alt', 'shift', 'win']:
    keyboard.release(key)
```

## Common Issues

❌ **"Blackout doesn't work"** → Run `python build_dummy.py` to create Dummy.exe
❌ **"Hotkeys not working in some apps"** → Run app.py as Administrator
❌ **"Dummy doesn't close on mouse movement"** → Check pynput is installed and dummy uses global hooks
❌ **"Config not saving"** → Stop monitoring before changing settings

## Testing Approach

1. **Hotkeys**: Start monitoring → test each hotkey → stop monitoring
2. **Shutdown**: Test countdown → press again to cancel → verify 5s cooldown
3. **Blackout**: Check Dummy.exe launches → monitors turn off → move mouse → verify dummy closes
4. **Config**: Change hotkeys → save → restart → verify persistence

## Dependencies

- **customtkinter**: Modern GUI (dark/light themes)
- **keyboard**: Global hotkey detection with suppression
- **pynput**: System-wide mouse/keyboard hooks (dummy.py only)
- **pyinstaller**: Compiling dummy.py to .exe

## Platform

Windows 10/11 only (uses Win32 APIs for monitor control and shutdown commands).
