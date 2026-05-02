#define MyAppName "System Squire"
#define MyAppPublisher "Jesse Loewen"
#define MyAppURL "https://github.com/jesseloewen/System-Squire"
#define MyAppExeName "System Squire.exe"

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\\dist"
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

[Setup]
AppId={{6C41D8D8-8F76-48CA-A3A2-9CF9BEA51B5C}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\\System Squire
DefaultGroupName=System Squire
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir={#OutputDir}
OutputBaseFilename=SystemSquireSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\\SystemSquire\\Assets\\system-squire-icon.ico
UninstallDisplayIcon={app}\\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\System Squire"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\System Squire"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "Launch System Squire"; Flags: nowait postinstall skipifsilent
