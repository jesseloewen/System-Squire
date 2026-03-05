"""
Build script for System Squire.exe
Compiles app.py into a standalone executable with all dependencies
"""

import subprocess
import os
import shutil
import sys

def build_dummy_exe():
    """Build Dummy.exe as part of the app build process"""
    print("  Compiling Dummy.exe...")
    
    try:
        result = subprocess.run([
            sys.executable, "-m", "PyInstaller",
            "--onefile",
            "--windowed",
            "--name", "Dummy",
            "dummy.py"
        ], check=True, capture_output=True, text=True)
        
        # Check if Dummy.exe was created
        dummy_source = os.path.join("dist", "Dummy.exe")
        if os.path.exists(dummy_source):
            print("  ✅ Dummy.exe compiled successfully")
            return True
        else:
            print("  ❌ Dummy.exe not found after compilation")
            return False
            
    except subprocess.CalledProcessError as e:
        print(f"  ❌ Dummy.exe compilation failed: {e}")
        return False

def build_app():
    """Build System Squire.exe from app.py"""
    print("🔨 Building System Squire.exe...")
    print("-" * 50)
    
    # Check if app.py exists
    if not os.path.exists("app.py"):
        print("❌ Error: app.py not found!")
        return False
    
    # Check if dummy.py exists
    if not os.path.exists("dummy.py"):
        print("❌ Error: dummy.py not found!")
        print("   Dummy.py is required for the blackout feature")
        return False
    
    # Build Dummy.exe first
    print("\n📦 Step 1: Building Dummy.exe...")
    if not build_dummy_exe():
        print("❌ Failed to build Dummy.exe")
        return False
    
    # Build with PyInstaller
    print("\n📦 Step 2: Building System Squire.exe...")
    
    # Build command
    build_cmd = [
        sys.executable, "-m", "PyInstaller",
        "--onefile",
        "--windowed",
        "--name", "System Squire",
    ]
    
    # Add icon if it exists
    if os.path.exists("icon.ico"):
        build_cmd.extend(["--icon", "icon.ico"])
        print("  Using icon.ico")
    
    # Add app.py at the end
    build_cmd.append("app.py")
    
    try:
        result = subprocess.run(build_cmd, check=True, capture_output=True, text=True)
        print("✅ Compilation successful!")
    except subprocess.CalledProcessError as e:
        print(f"❌ Compilation failed: {e}")
        print(e.stdout)
        print(e.stderr)
        return False
    
    # Check if executable was created
    source = os.path.join("dist", "System Squire.exe")
    
    if not os.path.exists(source):
        print("❌ Error: System Squire.exe not found in dist folder")
        return False
    
    print(f"✅ System Squire.exe created in dist/")
    
    # Verify Dummy.exe is in dist folder
    dummy_in_dist = os.path.join("dist", "Dummy.exe")
    if os.path.exists(dummy_in_dist):
        print(f"✅ Dummy.exe is in dist/ folder")
    else:
        print("⚠️  Warning: Dummy.exe not found in dist folder")
    
    # Cleanup build artifacts (keep dist folder with both executables)
    print("\n🧹 Cleaning up build artifacts...")
    cleanup_dirs = ["build"]
    cleanup_files = ["System Squire.spec", "Dummy.spec"]
    
    for directory in cleanup_dirs:
        if os.path.exists(directory):
            shutil.rmtree(directory)
            print(f"  Removed {directory}/")
    
    for file in cleanup_files:
        if os.path.exists(file):
            os.remove(file)
            print(f"  Removed {file}")
    
    print("-" * 50)
    print("✨ Build complete!")
    print(f"📁 Executable location: dist/System Squire.exe")
    print(f"📁 Dummy executable:    dist/Dummy.exe")
    print()
    print("ℹ️  To run the application:")
    print("   1. Navigate to the dist folder")
    print("   2. Run 'System Squire.exe'")
    print("   3. Dummy.exe must stay in the same folder")
    print("   4. For full functionality, run as Administrator")
    return True

if __name__ == "__main__":
    print("=" * 50)
    print("    System Squire Builder")
    print("=" * 50)
    print()
    
    success = build_app()
    
    if not success:
        print()
        print("❌ Build failed!")
        sys.exit(1)
    else:
        print()
        print("🎉 Build successful!")
        sys.exit(0)
