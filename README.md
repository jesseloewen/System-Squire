# System Squire 🛡️

**Advanced System Control & Hotkey Manager** - A modern Python desktop application for managing system shutdown and monitor blackout functionality with fully configurable global hotkeys.

## Features

### 🎯 Core Functionality
- **Smart Shutdown**: Initiate system shutdown with a 10-second countdown. Press the hotkey again to cancel with a 5-second cooldown period
- **Monitor Blackout**: Turn off your display while keeping the system running
- **External Detection**: Dummy.exe window allows external software to detect when monitors are off
- **Global Hotkeys**: System-wide hotkey detection that works even when the app is minimized
- **Hotkey Suppression**: Hotkeys are captured and not passed through to the active application
- **Configurable Keys**: Easy-to-use GUI for customizing hotkey combinations

### 🎨 Modern GUI
- **High-End Interface**: Built with CustomTkinter for a sleek, modern look
- **Dark/Light Mode**: Toggle between dark and light themes
- **Hotkey Recording**: Record hotkeys by pressing them - no typing required
- **Status Indicators**: Real-time visual feedback on monitoring status
- **Persistent Config**: Settings are saved and restored automatically

## Installation

### Prerequisites
- Windows 10/11
- Python 3.8 or higher
- Administrator privileges (recommended for full functionality)

### Setup Steps

1. **Clone or download this repository**
   ```powershell
   cd c:\Users\loewe\Documents\Coding\System-Squire
   ```

2. **Install Python dependencies**
   ```powershell
   pip install -r requirements.txt
   ```

3. **Build Dummy.exe** (required for blackout detection)
   ```powershell
   python build_dummy.py
   ```
   
   Or manually:
   ```powershell
   pyinstaller --onefile --windowed --name Dummy dummy.py
   # Move the executable
   mkdir scripts
   move dist\Dummy.exe scripts\Dummy.exe
   ```

4. **Run the application**
   ```powershell
   python app.py
   ```
   
   For full functionality, run as administrator:
   ```powershell
   # Right-click PowerShell and "Run as Administrator"
   python app.py
   ```

## Usage

### Getting Started

1. **Launch System Squire** - Run `python app.py`
2. **Configure Hotkeys** (optional):
   - Click "⏺ Record" next to each hotkey
   - Press your desired key combination
   - Click "Done" when finished
   - Click "💾 Save Configuration"
3. **Start Monitoring** - Click "▶ Start Monitoring"
4. **Use Your Hotkeys**:
   - Press shutdown hotkey once to initiate 10-second countdown
   - Press again during countdown to cancel (5-second cooldown applies)
   - Press blackout hotkey to turn off monitors instantly

### Default Hotkeys

- **Shutdown**: `Ctrl+Alt+F8`
- **Blackout**: `Ctrl+Alt+F7`

### Shutdown Behavior

- **First Press**: Initiates hybrid shutdown with 10-second timer
- **Second Press** (during countdown): Cancels shutdown
- **Cooldown**: After canceling, 5-second cooldown prevents accidental re-triggering

### Blackout Behavior

- Runs `Dummy.exe` as a visible window that appears in the taskbar
- Dummy becomes the **active/foreground window**
- Waits 300ms for dummy to fully activate
- Sends power-off command to all connected monitors
- **Dummy stays open while monitors are off** - allowing external software to detect it
- Uses **system-wide hooks** to detect mouse movement **anywhere on screen** (not just over the window)
- When you move the mouse anywhere or press any key (waking monitors), dummy closes automatically
- System remains fully operational throughout

## Configuration

Settings are stored in `config.json` and include:
- Shutdown hotkey combination
- Blackout hotkey combination  
- Theme preference (dark/light)
- Color theme

The configuration file is created automatically on first run and updated when you save changes in the GUI.

## Building Executables

### Build Standalone EXE for Main Application

Use the provided build script (recommended):
```powershell
python build_app.py
```

This will:
- Compile Dummy.exe automatically
- Compile app.py into System Squire.exe
- Place both executables in the `dist` folder
- Add icon if available
- Clean up build artifacts automatically

**Important:** Both `System Squire.exe` and `Dummy.exe` must stay together in the same folder.

Or build manually:
```powershell
# Build Dummy.exe first
pyinstaller --onefile --windowed --name Dummy dummy.py
# Build System Squire.exe
pyinstaller --onefile --windowed --name "System Squire" app.py
```

The executables will be in the `dist` folder.

### Build Dummy.exe

Use the provided build script:
```powershell
python build_dummy.py
```

Or manually:
```powershell
pyinstaller --onefile --windowed --name Dummy dummy.py
mkdir scripts
move dist\Dummy.exe scripts\Dummy.exe
```

## Project Structure

```
System-Squire/
├── app.py               # Main application with GUI
├── dummy.py             # Visible detection window (closes on any input)
├── build_app.py         # Script to build System Squire.exe
├── build_dummy.py       # Script to build Dummy.exe
├── requirements.txt     # Python dependencies
├── config.json          # Configuration file (auto-generated)
├── scripts/
│   └── Dummy.exe       # Compiled dummy executable
├── dist/                # Build output folder (after running build_app.py)
│   └── System Squire.exe
├── FUNCTIONALITY.md     # Original functionality specification
└── README.md           # This file
```

## Technical Details

### Dependencies
- **customtkinter**: Modern GUI framework
- **keyboard**: Global hotkey detection
- **pynput**: System-wide mouse and keyboard monitoring for dummy window
- **pyinstaller**: Compile Python to executable

### System Commands
- `shutdown /sg /t 10` - Hybrid shutdown with 10-second timer
- `shutdown /a` - Abort shutdown
- `SendMessageTimeoutW` with `SC_MONITORPOWER` - Monitor power control

### Dummy.exe Behavior & External Detection
- Creates a **visible 400x300 window** (black background with white text) that appears in the taskbar
- Window is centered on screen and stays on top
- **Global Input Detection**: Uses system-wide hooks to detect mouse/keyboard input **anywhere on screen**
- **Purpose**: Allows external software to detect when monitors are off by checking if dummy window exists
- **Detection Window**: Stays open and active while monitors are off
- **Closes on Input**: Any keyboard press, mouse click, or mouse movement **anywhere** closes the dummy
- External software can check: `If dummy.exe window/process exists → monitors are OFF`
- When dummy closes → monitors are being woken up

### Permissions
The application works without admin rights, but may have limited functionality. Run as administrator for:
- Reliable global hotkey detection
- System shutdown commands
- Monitor control in all scenarios

## Troubleshooting

### Hotkeys Not Working
- Ensure you clicked "▶ Start Monitoring"
- Check if another application is using the same hotkey
- Try running as administrator
- Re-record the hotkey if it contains special characters

### Dummy.exe Missing
- Run `python build_dummy.py` to create it
- Ensure it's in the `scripts/` folder
- The blackout feature will still work without it, but won't trigger detection

### Configuration Not Saving
- Check file permissions in the application directory
- Ensure `config.json` isn't set to read-only
- Try running as administrator

### Monitor Won't Turn Off
- Some monitors don't respond to software commands
- Try updating your graphics drivers
- Check monitor settings for "DDC/CI" or similar options

## Development

### Modifying the GUI
The GUI is built with CustomTkinter. Main sections:
- Status indicators and control buttons
- Hotkey configuration with recording
- Theme selection

### Adding New Functions
1. Add the function to the `SystemSquire` class
2. Create a hotkey entry in the GUI
3. Register the hotkey in `register_hotkeys()`
4. Update `DEFAULT_CONFIG` with the new hotkey

### Custom Themes
CustomTkinter supports these color themes:
- blue (default)
- green
- dark-blue

Change in code or add to config:
```python
ctk.set_default_color_theme("green")
```

## License

This project is provided as-is for personal use. Modify and distribute freely.

## Credits

Created as a system utility for advanced Windows power users who want quick access to system controls.

---

**⚠️ Important**: Always ensure you have saved your work before using the shutdown feature. The 10-second countdown provides time to cancel, but be mindful of open applications.
