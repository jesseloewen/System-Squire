# System Squire - Workspace Guidelines

Windows desktop application for global hotkey control using C# WPF with low-level keyboard hooks.

## Architecture

**Two independent executables:**
- `System Squire.exe` - Main WPF app with tray integration and keyboard hook
- `Dummy.exe` - Helper window for monitor blackout detection

Key classes (one per file):
- [KeyboardHook.cs](../SystemSquire/KeyboardHook.cs) - `SetWindowsHookEx(WH_KEYBOARD_LL)` for exact hotkey matching
- [SystemOperations.cs](../SystemSquire/SystemOperations.cs) - Shutdown countdown and monitor control
- [ConfigManager.cs](../SystemSquire/ConfigManager.cs) - JSON persistence in exe directory
- [MainWindow.xaml(.cs)](../SystemSquire/MainWindow.xaml) - WPF UI, tray icon, hotkey recording

**Critical dependency:** `Dummy.exe` must be in same directory as main exe.

## Build and Test

```powershell
# Build and package (recommended)
.\build.ps1

# Or dotnet CLI
dotnet build SystemSquire.sln --configuration Release

# Run
.\dist\System Squire.exe
```

**Testing requirements:**
- Windows 10/11 only (Windows API dependencies)
- Admin rights recommended for system-wide hotkey capture
- Manual testing only - requires real keyboard/mouse input

## Critical Patterns

**Hook Thread Safety:**
- NEVER block in `HookCallback` - must call `CallNextHookEx` quickly
- Use `Task.Run(() => callback())` for async execution
- Always unhook in `Dispose()` to prevent system instability

**Window Lifecycle:**
- Close button minimizes to tray (`e.Cancel = true` in `Window_Closing`)
- Only tray context menu "Exit" properly shuts down
- Single instance enforced via named Mutex

**Exact Hotkey Matching:**
- Tracks all pressed keys via `HashSet<Key>`
- Only triggers on exact modifier+key combinations
- `Ctrl+Alt+F8` will NOT trigger on `Ctrl+F8` or `Ctrl+Alt+F9`

## Code Style

- PascalCase public members, `_camelCase` private fields
- XML doc comments on all classes
- `#region` blocks for P/Invoke declarations
- Nullable reference types enabled
- Event-driven with proper `IDisposable` implementation

## Common Pitfalls

1. **Admin Rights:** App works without elevation but can't detect hotkeys in elevated apps (UAC boundary)
2. **Hook Recording:** Must temporarily uninstall keyboard hook during hotkey recording UI
3. **Tray Disposal:** Tray icon defined in XAML resources as `TrayIcon` - must dispose on exit
4. **Cross-thread UI:** Use `Dispatcher.Invoke()` for async callback → UI updates
5. **Dummy.exe Path:** Hardcoded as `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dummy.exe")`

## Technologies

- .NET 8 WPF (`net8.0-windows`)
- Windows API: `SetWindowsHookEx`, `SendMessage`, `FindWindow`
- NuGet: `Newtonsoft.Json` 13.0.3, `Hardcodet.NotifyIcon.Wpf` 1.1.0
