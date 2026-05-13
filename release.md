# System Squire v1.2.3

Blackout stability fixes. Better dummy window focus handling. Smoother UI status updates.

[Download v1.2.3 Installer](https://github.com/jesseloewen/System-Squire/releases/download/v1.2.3/SystemSquireSetup-1.2.3.exe)

## Why this is v1.2.3

This patch release focuses on bug fixes and reliability improvements after v1.2.2:

- Prevents blackout hangs caused by unresponsive windows or display drivers during monitor power-off
- Improves dummy window foreground activation reliability during blackout workflows
- Reduces risk of UI stalls when processing status updates

## What is fixed

- Reworked blackout monitor power-off signaling to use timeout-safe messaging, avoiding hangs when broadcast targets are unresponsive
- Added warning logging when monitor power-off signaling times out or fails
- Improved dummy window foreground acquisition with stronger focus handoff logic and more stable activation timing
- Increased dummy window post-activation settle timing for more consistent behavior
- Updated main window status updates to use non-blocking UI dispatch paths for better responsiveness

## Quick summary for users

If you are already on System Squire, v1.2.3 is a reliability update that makes blackout behavior safer, dummy window activation more consistent, and the UI less likely to stall during state changes.

## Install

1. Download the installer from the link above.
2. Run the installer.
3. Launch System Squire from the Start menu.

Windows 10/11 recommended.
