"""
Dummy.exe - Creates a visible window for external detection
Opens as a proper window with taskbar presence, stays open until any user input
Uses global mouse/keyboard hooks to detect input anywhere on screen
Used for external detection - when dummy is running, monitors are off
"""

import tkinter as tk
import sys
import threading
from pynput import mouse, keyboard as kb

def create_dummy_window():
    """Create a visible window that appears in taskbar. Closes on any user input (anywhere)."""
    root = tk.Tk()
    root.title("Dummy")
    
    # Create a normal-sized window centered on screen
    window_width = 400
    window_height = 300
    screen_width = root.winfo_screenwidth()
    screen_height = root.winfo_screenheight()
    x = (screen_width - window_width) // 2
    y = (screen_height - window_height) // 2
    root.geometry(f"{window_width}x{window_height}+{x}+{y}")
    
    # Make it a proper visible window (appears in taskbar)
    root.attributes('-topmost', True)  # Keep on top
    root.configure(bg='black')  # Black background
    
    # Add a simple label to show it's active
    label = tk.Label(
        root,
        text="Dummy Window\n\n(Active for Detection)\n\nPress any key or move mouse to close",
        font=('Arial', 14),
        bg='black',
        fg='white',
        justify='center'
    )
    label.pack(expand=True, fill='both')
    
    # Bring window to focus and make it the active window
    root.focus_force()
    root.lift()
    root.update()  # Ensure window is fully created
    
    def close_window(event=None):
        """Close the window on any user input"""
        try:
            root.quit()
            root.destroy()
        except:
            pass
    
    # Global mouse listener for system-wide mouse movement detection
    def on_mouse_move(x, y):
        """Called when mouse moves anywhere on screen"""
        close_window()
        return False  # Stop listener
    
    def on_mouse_click(x, y, button, pressed):
        """Called when mouse button is pressed"""
        if pressed:
            close_window()
            return False  # Stop listener
    
    def on_mouse_scroll(x, y, dx, dy):
        """Called when mouse wheel is scrolled"""
        close_window()
        return False  # Stop listener
    
    # Global keyboard listener for system-wide keyboard detection
    def on_key_press(key):
        """Called when any key is pressed"""
        close_window()
        return False  # Stop listener
    
    # Start mouse listener in separate thread
    mouse_listener = mouse.Listener(
        on_move=on_mouse_move,
        on_click=on_mouse_click,
        on_scroll=on_mouse_scroll
    )
    mouse_listener.start()
    
    # Start keyboard listener in separate thread
    keyboard_listener = kb.Listener(on_press=on_key_press)
    keyboard_listener.start()
    
    # Bind window close button
    root.protocol("WM_DELETE_WINDOW", close_window)
    
    try:
        root.mainloop()
    finally:
        # Ensure listeners are stopped
        try:
            mouse_listener.stop()
            keyboard_listener.stop()
        except:
            pass

if __name__ == "__main__":
    create_dummy_window()
    sys.exit(0)
