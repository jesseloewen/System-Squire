# System Squire v1.2.1

Sharper web controls. Better visibility. Smoother window behavior.

[Download v1.2.1 Installer](https://github.com/jesseloewen/System-Squire/releases/download/v1.2.1/SystemSquireSetup-1.2.1.exe)

## Why this is v1.2.1

This release focuses on usability and reliability improvements introduced after v1.2.0:

- Better remote web service control and clearer running endpoint feedback
- Improved tray-to-window restore behavior so the app reliably comes to the front
- More consistent launch-app monitoring behavior for minimize automation

## What is new

- Updated web service control row in the main UI with state-based actions
- Start now appears as a single primary action when stopped
- Stop/Restart/Open actions are shown when the service is running
- Added direct Open action in the web service controls for faster access to the remote page
- Web service status now reports a practical LAN IPv4 endpoint (with port) when available, improving discoverability from other devices
- Clicking the tray balloon tip now restores the main window
- Main window foreground activation was strengthened to reduce cases where restore does not surface the window
- Launch-app watcher logic was simplified for more reliable detection of app windows during monitoring
- Pushover button naming was streamlined to "Pushover Notifications" in the app and README for consistency

## Quick summary for users

If you are already on System Squire, v1.2.1 makes remote control easier to use day to day and improves window restore reliability when interacting from the tray.

## Install

1. Download the installer from the link above.
2. Run the installer.
3. Launch System Squire from the Start menu.

Windows 10/11 recommended.
