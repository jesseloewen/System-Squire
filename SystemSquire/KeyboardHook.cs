using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SystemSquire
{
    /// <summary>
    /// Low-level keyboard hook that captures specific key combinations
    /// Only triggers on EXACT matches - Ctrl+Alt+F8 won't trigger on just Ctrl or Alt
    /// </summary>
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly Dictionary<HotkeyDefinition, Action> _hotkeys = new();
        
        // Track current pressed keys
        private readonly HashSet<Key> _pressedKeys = new();
        
        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void InstallHook()
        {
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }
        }

        public void UninstallHook()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        public void RegisterHotkey(ModifierKeys modifiers, Key key, Action callback)
        {
            var hotkey = new HotkeyDefinition(modifiers, key);
            _hotkeys[hotkey] = callback;
        }

        public void UnregisterHotkey(ModifierKeys modifiers, Key key)
        {
            var hotkey = new HotkeyDefinition(modifiers, key);
            _hotkeys.Remove(hotkey);
        }

        public void ClearHotkeys()
        {
            _hotkeys.Clear();
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                if (curModule?.ModuleName != null)
                {
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc, 
                        GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                // Add to pressed keys
                _pressedKeys.Add(key);

                // Get current modifiers from pressed keys
                ModifierKeys currentModifiers = GetCurrentModifiers();

                // Check for exact hotkey match
                var currentHotkey = new HotkeyDefinition(currentModifiers, key);
                
                if (_hotkeys.TryGetValue(currentHotkey, out var callback))
                {
                    // Execute callback asynchronously to avoid blocking the hook
                    System.Threading.Tasks.Task.Run(callback);
                    
                    // Return 1 to suppress this key combination
                    return (IntPtr)1;
                }
            }
            else if (nCode >= 0)
            {
                // Key up event - remove from pressed keys
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);
                _pressedKeys.Remove(key);
            }

            // Pass to next hook
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private ModifierKeys GetCurrentModifiers()
        {
            ModifierKeys modifiers = ModifierKeys.None;

            if (_pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl))
                modifiers |= ModifierKeys.Control;
            if (_pressedKeys.Contains(Key.LeftAlt) || _pressedKeys.Contains(Key.RightAlt))
                modifiers |= ModifierKeys.Alt;
            if (_pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift))
                modifiers |= ModifierKeys.Shift;
            if (_pressedKeys.Contains(Key.LWin) || _pressedKeys.Contains(Key.RWin))
                modifiers |= ModifierKeys.Windows;

            return modifiers;
        }

        public void Dispose()
        {
            UninstallHook();
        }

        #region Native Methods
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, 
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion
    }

    /// <summary>
    /// Defines a hotkey combination (modifiers + key)
    /// </summary>
    public struct HotkeyDefinition : IEquatable<HotkeyDefinition>
    {
        public ModifierKeys Modifiers { get; }
        public Key Key { get; }

        public HotkeyDefinition(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public override bool Equals(object? obj)
        {
            return obj is HotkeyDefinition definition && Equals(definition);
        }

        public bool Equals(HotkeyDefinition other)
        {
            return Modifiers == other.Modifiers && Key == other.Key;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Modifiers, Key);
        }

        public override string ToString()
        {
            var parts = new List<string>();
            
            if (Modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");
            
            parts.Add(Key.ToString());
            
            return string.Join("+", parts);
        }

        public static HotkeyDefinition? Parse(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
                return null;

            var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return null;

            ModifierKeys modifiers = ModifierKeys.None;
            Key? mainKey = null;

            foreach (var part in parts)
            {
                var lowerPart = part.ToLower();
                switch (lowerPart)
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModifierKeys.Control;
                        break;
                    case "alt":
                        modifiers |= ModifierKeys.Alt;
                        break;
                    case "shift":
                        modifiers |= ModifierKeys.Shift;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= ModifierKeys.Windows;
                        break;
                    default:
                        // Try to parse as a key
                        if (Enum.TryParse<Key>(part, true, out var parsedKey))
                        {
                            mainKey = parsedKey;
                        }
                        break;
                }
            }

            if (mainKey.HasValue)
            {
                return new HotkeyDefinition(modifiers, mainKey.Value);
            }

            return null;
        }
    }
}
