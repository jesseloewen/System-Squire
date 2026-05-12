# System Squire v1.2.2

Smarter blackout controls. New dummy window support. Better update awareness.

[Download v1.2.2 Installer](https://github.com/jesseloewen/System-Squire/releases/download/v1.2.2/SystemSquireSetup-1.2.2.exe)

## Why this is v1.2.2

This release focuses on blackout reliability, remote control flexibility, and update flow improvements after v1.2.1:

- Added finer blackout behavior controls for keyboard lock keys
- Introduced a dedicated dummy window tool for blackout-related workflows
- Improved update detection and install flow behavior in the main UI

## What is new

- Added blackout options to automatically turn off Caps Lock, Num Lock, and Scroll Lock when blackout is triggered
- Added an option to open a dedicated dummy window during blackout scenarios
- Added remote web API support to open the dummy window directly
- Added main window controls to open the dummy window and copy its executable path
- Strengthened blackout trigger handling through UI-dispatch-safe execution paths
- Improved cleanup on shutdown to stop blackout restore watchers and close dummy windows more reliably
- Improved GitHub release checks to consider a broader release set and better handle stable versus pre-release version states
- Improved update button behavior so Check, Download, and Install actions reflect the current update state more clearly
- Added a new SystemSquireDummyWindow project and included its built executable in the packaged Tools folder

## Quick summary for users

If you are already on System Squire, v1.2.2 gives you more control over blackout behavior, adds new remote and UI support for a dummy window tool, and makes update handling clearer and more reliable.

## Install

1. Download the installer from the link above.
2. Run the installer.
3. Launch System Squire from the Start menu.

Windows 10/11 recommended.
