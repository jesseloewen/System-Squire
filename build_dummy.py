"""
Build script for Dummy.exe
Compiles dummy.py into a standalone executable
"""

import subprocess
import os
import shutil
import sys

def build_dummy():
    """Build Dummy.exe from dummy.py"""
    print("🔨 Building Dummy.exe...")
    print("-" * 50)
    
    # Check if dummy.py exists
    if not os.path.exists("dummy.py"):
        print("❌ Error: dummy.py not found!")
        return False
    
    # Build with PyInstaller
    print("📦 Compiling with PyInstaller...")
    try:
        result = subprocess.run([
            sys.executable, "-m", "PyInstaller",
            "--onefile",
            "--windowed",
            "--name", "Dummy",
            "dummy.py"
        ], check=True, capture_output=True, text=True)
        print("✅ Compilation successful!")
    except subprocess.CalledProcessError as e:
        print(f"❌ Compilation failed: {e}")
        print(e.stdout)
        print(e.stderr)
        return False
    
    # Create scripts directory
    if not os.path.exists("scripts"):
        os.makedirs("scripts")
        print("📁 Created scripts directory")
    
    # Move the executable
    source = os.path.join("dist", "Dummy.exe")
    dest = os.path.join("scripts", "Dummy.exe")
    
    if os.path.exists(source):
        shutil.move(source, dest)
        print(f"✅ Moved Dummy.exe to scripts/")
    else:
        print("❌ Error: Dummy.exe not found in dist folder")
        return False
    
    # Cleanup build artifacts
    print("🧹 Cleaning up build artifacts...")
    cleanup_dirs = ["build", "dist"]
    cleanup_files = ["Dummy.spec"]
    
    for directory in cleanup_dirs:
        if os.path.exists(directory):
            shutil.rmtree(directory)
            print(f"  Removed {directory}/")
    
    for file in cleanup_files:
        if os.path.exists(file):
            os.remove(file)
            print(f"  Removed {file}")
    
    print("-" * 50)
    print("✨ Build complete! Dummy.exe is ready in scripts/")
    return True

if __name__ == "__main__":
    print("=" * 50)
    print("    Dummy.exe Builder")
    print("=" * 50)
    print()
    
    success = build_dummy()
    
    print()
    if success:
        print("✅ SUCCESS: Dummy.exe is ready to use!")
        print("   Location: scripts/Dummy.exe")
    else:
        print("❌ FAILED: Could not build Dummy.exe")
        print("   Please check the errors above")
    print()
    print("=" * 50)
    
    sys.exit(0 if success else 1)
