"""
System Squire - Advanced System Control Application
Manages system shutdown and monitor blackout with configurable hotkeys
"""

import customtkinter as ctk
import keyboard
import subprocess
import threading
import time
import json
import os
import sys
from tkinter import messagebox
from ctypes import windll, c_wchar_p
import pystray
from PIL import Image, ImageDraw

# Configuration
CONFIG_FILE = "config.json"
DEFAULT_CONFIG = {
    "shutdown_hotkey": "ctrl+alt+f8",
    "blackout_hotkey": "ctrl+alt+f7",
    "theme": "dark",
    "color_theme": "blue"
}

class SystemSquire:
    def __init__(self):
        self.config = self.load_config()
        self.shutdown_active = False
        self.shutdown_timer = None
        self.cooldown_active = False
        self.hotkeys_registered = False
        self.dummy_path = self._get_dummy_path()
    
    def _get_dummy_path(self):
        """Get the correct path to Dummy.exe based on execution mode"""
        if getattr(sys, 'frozen', False):
            # Running as compiled exe - Dummy.exe is in the same directory
            application_path = os.path.dirname(sys.executable)
            dummy_path = os.path.join(application_path, "Dummy.exe")
        else:
            # Running as script - Dummy.exe is in scripts folder
            dummy_path = os.path.join("scripts", "Dummy.exe")
        return dummy_path
        
    def load_config(self):
        """Load configuration from file or create default"""
        if os.path.exists(CONFIG_FILE):
            try:
                with open(CONFIG_FILE, 'r') as f:
                    config = json.load(f)
                    # Merge with defaults for any missing keys
                    return {**DEFAULT_CONFIG, **config}
            except:
                return DEFAULT_CONFIG.copy()
        return DEFAULT_CONFIG.copy()
    
    def save_config(self):
        """Save configuration to file"""
        with open(CONFIG_FILE, 'w') as f:
            json.dump(self.config, f, indent=4)
    
    def shutdown_system(self):
        """Initiate system shutdown with 10-second timer"""
        if self.cooldown_active:
            return
        
        if self.shutdown_active:
            # Cancel shutdown
            self.cancel_shutdown()
        else:
            # Start shutdown
            self.shutdown_active = True
            try:
                subprocess.run(['shutdown', '/sg', '/t', '10'], check=True)
                print("Shutdown initiated - 10 seconds")
            except Exception as e:
                print(f"Error initiating shutdown: {e}")
                self.shutdown_active = False
    
    def cancel_shutdown(self):
        """Cancel pending shutdown and start cooldown"""
        try:
            subprocess.run(['shutdown', '/a'], check=True)
            print("Shutdown cancelled")
            self.shutdown_active = False
            self.start_cooldown()
        except Exception as e:
            print(f"Error cancelling shutdown: {e}")
    
    def start_cooldown(self):
        """Start 5-second cooldown period"""
        self.cooldown_active = True
        threading.Thread(target=self._cooldown_timer, daemon=True).start()
    
    def _cooldown_timer(self):
        """Cooldown timer thread"""
        time.sleep(5)
        self.cooldown_active = False
        print("Cooldown complete")
    
    def _wait_for_dummy_window(self, timeout=3.0):
        """Wait for Dummy window to appear and activate it"""
        start_time = time.time()
        while time.time() - start_time < timeout:
            # Check if window with title "Dummy" exists
            hwnd = windll.user32.FindWindowW(None, c_wchar_p("Dummy"))
            if hwnd != 0:
                print(f"Dummy window found (hwnd: {hwnd})")
                
                # Activate the window to bring it to foreground
                windll.user32.SetForegroundWindow(hwnd)
                windll.user32.ShowWindow(hwnd, 9)  # SW_RESTORE = 9
                
                # Give it a bit more time to fully activate
                time.sleep(0.3)
                return hwnd
            time.sleep(0.1)  # Check every 100ms
        
        print("Warning: Dummy window not found within timeout")
        return 0
    
    def blackout_screen(self):
        """Turn off monitor display"""
        try:
            # Run dummy.exe first and wait for it to become active
            if os.path.exists(self.dummy_path):
                print(f"Launching Dummy from: {self.dummy_path}")
                subprocess.Popen([self.dummy_path])
                
                # Wait for dummy window to actually open and activate it
                hwnd = self._wait_for_dummy_window(timeout=3.0)
                if hwnd == 0:
                    print("Warning: Proceeding with blackout despite dummy window not detected")
            else:
                print(f"Warning: Dummy.exe not found at {self.dummy_path}")
            
            # Release all modifier keys to prevent them from getting stuck
            # This is critical when using hotkeys with modifiers (ctrl, alt, etc.)
            modifier_keys = ['ctrl', 'alt', 'shift', 'win']
            for key in modifier_keys:
                try:
                    keyboard.release(key)
                except:
                    pass
            
            # Small delay to ensure keys are released
            time.sleep(0.1)
            
            # Send monitor power off command
            # SC_MONITORPOWER = 0xF170
            # MONITOR_OFF = 2
            # windll.user32.SendMessageTimeoutW(
            #     0xFFFF,  # HWND_BROADCAST
            #     0x0112,  # WM_SYSCOMMAND
            #     SC_MONITORPOWER,
            #     MONITOR_OFF,
            #     0x0002,  # SMTO_ABORTIFHUNG
            #     1000,
            #     None
            # )
            print("Monitor blackout activated")
        except Exception as e:
            print(f"Error during blackout: {e}")
    
    def register_hotkeys(self):
        """Register global hotkeys with suppression"""
        if self.hotkeys_registered:
            self.unregister_hotkeys()
        
        try:
            # suppress=True prevents the hotkey from being passed to the active window
            keyboard.add_hotkey(self.config['shutdown_hotkey'], self.shutdown_system, suppress=True)
            keyboard.add_hotkey(self.config['blackout_hotkey'], self.blackout_screen, suppress=True)
            self.hotkeys_registered = True
            print(f"Hotkeys registered: Shutdown={self.config['shutdown_hotkey']}, Blackout={self.config['blackout_hotkey']}")
        except Exception as e:
            print(f"Error registering hotkeys: {e}")
            raise
    
    def unregister_hotkeys(self):
        """Unregister all hotkeys"""
        try:
            keyboard.unhook_all()
            self.hotkeys_registered = False
            print("Hotkeys unregistered")
        except Exception as e:
            print(f"Error unregistering hotkeys: {e}")


class SystemSquireGUI:
    def __init__(self):
        # Initialize system manager
        self.system = SystemSquire()
        
        # Setup CustomTkinter
        ctk.set_appearance_mode(self.system.config.get('theme', 'dark'))
        ctk.set_default_color_theme(self.system.config.get('color_theme', 'blue'))
        
        # Create main window
        self.root = ctk.CTk()
        self.root.title("System Squire")
        self.root.geometry("700x550")
        self.root.resizable(False, False)
        
        # Set icon if available
        try:
            self.root.iconbitmap('icon.ico')
        except:
            pass
        
        # Tray icon setup
        self.tray_icon = None
        self.setup_tray_icon()
        
        self.setup_ui()
        
        # Handle window close
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
        
        # Start monitoring automatically
        self.start_monitoring_auto()
        
        # Start minimized to tray
        self.root.withdraw()
    
    def setup_ui(self):
        """Setup the user interface"""
        # Main container with padding
        main_frame = ctk.CTkFrame(self.root)
        main_frame.pack(fill="both", expand=True, padx=20, pady=20)
        
        # Title
        title_label = ctk.CTkLabel(
            main_frame,
            text="⚙️ System Squire",
            font=ctk.CTkFont(size=32, weight="bold")
        )
        title_label.pack(pady=(10, 5))
        
        subtitle_label = ctk.CTkLabel(
            main_frame,
            text="Advanced System Control & Hotkey Manager",
            font=ctk.CTkFont(size=14),
            text_color="gray"
        )
        subtitle_label.pack(pady=(0, 20))
        
        # Status Frame
        status_frame = ctk.CTkFrame(main_frame)
        status_frame.pack(fill="x", pady=(0, 20))
        
        status_label = ctk.CTkLabel(
            status_frame,
            text="Status:",
            font=ctk.CTkFont(size=16, weight="bold")
        )
        status_label.pack(side="left", padx=15, pady=10)
        
        self.status_indicator = ctk.CTkLabel(
            status_frame,
            text="● Monitoring Active",
            font=ctk.CTkFont(size=16),
            text_color="#2ecc71"
        )
        self.status_indicator.pack(side="left", pady=10)
        
        # Hotkey Configuration Frame
        hotkey_frame = ctk.CTkFrame(main_frame)
        hotkey_frame.pack(fill="x", pady=(0, 20))
        
        hotkey_title = ctk.CTkLabel(
            hotkey_frame,
            text="Hotkey Configuration",
            font=ctk.CTkFont(size=18, weight="bold")
        )
        hotkey_title.pack(pady=(15, 10))
        
        # Shutdown Hotkey
        shutdown_container = ctk.CTkFrame(hotkey_frame, fg_color="transparent")
        shutdown_container.pack(fill="x", padx=15, pady=5)
        
        shutdown_label = ctk.CTkLabel(
            shutdown_container,
            text="Shutdown Hotkey:",
            font=ctk.CTkFont(size=14),
            width=150,
            anchor="w"
        )
        shutdown_label.pack(side="left", padx=(0, 10))
        
        self.shutdown_entry = ctk.CTkEntry(
            shutdown_container,
            font=ctk.CTkFont(size=14),
            height=35
        )
        self.shutdown_entry.pack(side="left", fill="x", expand=True, padx=(0, 10))
        self.shutdown_entry.insert(0, self.system.config['shutdown_hotkey'])
        
        record_shutdown_btn = ctk.CTkButton(
            shutdown_container,
            text="⏺ Record",
            command=lambda: self.record_hotkey(self.shutdown_entry),
            width=100,
            height=35
        )
        record_shutdown_btn.pack(side="left")
        
        # Blackout Hotkey
        blackout_container = ctk.CTkFrame(hotkey_frame, fg_color="transparent")
        blackout_container.pack(fill="x", padx=15, pady=5)
        
        blackout_label = ctk.CTkLabel(
            blackout_container,
            text="Blackout Hotkey:",
            font=ctk.CTkFont(size=14),
            width=150,
            anchor="w"
        )
        blackout_label.pack(side="left", padx=(0, 10))
        
        self.blackout_entry = ctk.CTkEntry(
            blackout_container,
            font=ctk.CTkFont(size=14),
            height=35
        )
        self.blackout_entry.pack(side="left", fill="x", expand=True, padx=(0, 10))
        self.blackout_entry.insert(0, self.system.config['blackout_hotkey'])
        
        record_blackout_btn = ctk.CTkButton(
            blackout_container,
            text="⏺ Record",
            command=lambda: self.record_hotkey(self.blackout_entry),
            width=100,
            height=35
        )
        record_blackout_btn.pack(side="left")
        
        # Save Config Button
        save_btn = ctk.CTkButton(
            hotkey_frame,
            text="💾 Save Configuration",
            command=self.save_configuration,
            font=ctk.CTkFont(size=14, weight="bold"),
            height=40
        )
        save_btn.pack(pady=15, padx=15, fill="x")
        
        # Theme Selection
        theme_frame = ctk.CTkFrame(main_frame)
        theme_frame.pack(fill="x")
        
        theme_label = ctk.CTkLabel(
            theme_frame,
            text="Theme:",
            font=ctk.CTkFont(size=14)
        )
        theme_label.pack(side="left", padx=15, pady=10)
        
        self.theme_switch = ctk.CTkSwitch(
            theme_frame,
            text="Dark Mode",
            command=self.toggle_theme,
            font=ctk.CTkFont(size=14)
        )
        self.theme_switch.pack(side="left", padx=10, pady=10)
        
        if self.system.config.get('theme', 'dark') == 'dark':
            self.theme_switch.select()
    
    def create_tray_image(self):
        """Create a simple icon for the system tray"""
        # Create a 64x64 image with a gear/settings icon
        width = 64
        height = 64
        image = Image.new('RGB', (width, height), color='black')
        dc = ImageDraw.Draw(image)
        
        # Draw a simple gear icon
        center = (width // 2, height // 2)
        outer_radius = 28
        inner_radius = 15
        
        # Draw outer circle
        dc.ellipse(
            [center[0] - outer_radius, center[1] - outer_radius,
             center[0] + outer_radius, center[1] + outer_radius],
            fill='#2ecc71', outline='white'
        )
        
        # Draw inner circle (hole)
        dc.ellipse(
            [center[0] - inner_radius, center[1] - inner_radius,
             center[0] + inner_radius, center[1] + inner_radius],
            fill='black', outline='white'
        )
        
        return image
    
    def setup_tray_icon(self):
        """Setup system tray icon with menu"""
        # Create tray icon menu
        menu = pystray.Menu(
            pystray.MenuItem('Show Window', self.show_window, default=True),
            pystray.MenuItem('Shutdown', self.tray_shutdown),
            pystray.MenuItem('Blackout', self.tray_blackout),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem('Exit', self.exit_app)
        )
        
        # Create and setup tray icon
        self.tray_icon = pystray.Icon(
            "System Squire",
            self.create_tray_image(),
            "System Squire - Monitoring Active",
            menu
        )
        
        # Run tray icon in a separate thread
        threading.Thread(target=self.tray_icon.run, daemon=True).start()
    
    def show_window(self, icon=None, item=None):
        """Show the main window from tray"""
        self.root.after(0, self._show_window)
    
    def _show_window(self):
        """Internal method to show window (needs to run in main thread)"""
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()
    
    def hide_window(self):
        """Hide window to tray"""
        self.root.withdraw()
    
    def tray_shutdown(self, icon=None, item=None):
        """Trigger shutdown from tray menu"""
        self.system.shutdown_system()
    
    def tray_blackout(self, icon=None, item=None):
        """Trigger blackout from tray menu"""
        self.system.blackout_screen()
    
    def exit_app(self, icon=None, item=None):
        """Completely exit the application"""
        self.root.after(0, self._exit_app)
    
    def _exit_app(self):
        """Internal method to exit app (needs to run in main thread)"""
        self.system.unregister_hotkeys()
        if self.tray_icon:
            self.tray_icon.stop()
        self.root.quit()
        self.root.destroy()
    
    def start_monitoring_auto(self):
        """Start hotkey monitoring automatically on startup"""
        try:
            self.system.register_hotkeys()
            print(f"Hotkeys registered: Shutdown={self.system.config['shutdown_hotkey']}, Blackout={self.system.config['blackout_hotkey']}")
        except Exception as e:
            messagebox.showerror("Error", f"Failed to start monitoring:\n{e}\n\nThe application will close.")
            self.root.destroy()
    
    def record_hotkey(self, entry_widget):
        """Record a new hotkey combination"""
        # Temporarily unregister hotkeys for recording
        self.system.unregister_hotkeys()
        
        # Create recording dialog
        dialog = ctk.CTkToplevel(self.root)
        dialog.title("Record Hotkey")
        dialog.geometry("400x200")
        dialog.transient(self.root)
        dialog.grab_set()
        
        label = ctk.CTkLabel(
            dialog,
            text="Press your desired key combination...",
            font=ctk.CTkFont(size=16)
        )
        label.pack(pady=30)
        
        recorded_label = ctk.CTkLabel(
            dialog,
            text="",
            font=ctk.CTkFont(size=14, weight="bold")
        )
        recorded_label.pack(pady=10)
        
        recorded_keys = []
        
        def on_key_event(e):
            if e.event_type == 'down' and e.name not in recorded_keys:
                recorded_keys.append(e.name)
                hotkey_str = '+'.join(recorded_keys)
                recorded_label.configure(text=hotkey_str)
        
        # Hook keyboard
        hook = keyboard.hook(on_key_event)
        
        def finish_recording():
            keyboard.unhook(hook)
            if recorded_keys:
                hotkey_str = '+'.join(recorded_keys)
                entry_widget.delete(0, 'end')
                entry_widget.insert(0, hotkey_str)
            dialog.destroy()
        
        def finish_and_reregister():
            finish_recording()
            # Re-register hotkeys after recording
            try:
                self.system.register_hotkeys()
            except Exception as e:
                messagebox.showerror("Error", f"Failed to re-register hotkeys: {e}")
        
        finish_btn = ctk.CTkButton(
            dialog,
            text="Done",
            command=finish_and_reregister,
            width=150,
            height=40
        )
        finish_btn.pack(pady=20)
    
    def save_configuration(self):
        """Save current configuration and restart monitoring"""
        # Temporarily unregister hotkeys
        self.system.unregister_hotkeys()
        
        # Update configuration
        self.system.config['shutdown_hotkey'] = self.shutdown_entry.get()
        self.system.config['blackout_hotkey'] = self.blackout_entry.get()
        self.system.save_config()
        
        # Re-register with new hotkeys
        try:
            self.system.register_hotkeys()
            messagebox.showinfo("Saved", "Configuration saved and hotkeys updated!")
        except Exception as e:
            messagebox.showerror("Error", f"Failed to register new hotkeys: {e}")
    
    def toggle_theme(self):
        """Toggle between light and dark theme"""
        new_theme = "dark" if self.theme_switch.get() else "light"
        ctk.set_appearance_mode(new_theme)
        self.system.config['theme'] = new_theme
    
    def on_closing(self):
        """Handle window closing - minimize to tray instead of closing"""
        self.hide_window()
    
    def run(self):
        """Run the application"""
        self.root.mainloop()


if __name__ == "__main__":
    # Check for admin rights
    try:
        is_admin = windll.shell32.IsUserAnAdmin()
        if not is_admin:
            print("Warning: Running without administrator privileges. Some features may not work.")
    except:
        pass
    
    # Create and run GUI
    app = SystemSquireGUI()
    app.run()
