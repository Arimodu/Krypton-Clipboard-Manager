[Setup]
AppName=Krypton Clipboard Manager
AppVersion={#AppVersion}
AppPublisher=Arimodu
AppPublisherURL=https://github.com/Arimodu/Krypton-Clipboard-Manager
AppSupportURL=https://github.com/Arimodu/Krypton-Clipboard-Manager/issues
AppUpdatesURL=https://github.com/Arimodu/Krypton-Clipboard-Manager/releases
DefaultDirName={autopf}\Krypton
DefaultGroupName=Krypton
OutputBaseFilename=krypton-desktop-win-x64-setup
LicenseFile=license.txt
PrivilegesRequired=admin
UninstallDisplayName=Krypton Clipboard Manager
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=
WizardStyle=modern
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Variant selection (mutually exclusive)
Name: "sc"; Description: "Self-contained (includes .NET 9 runtime, no prerequisites)"; \
  GroupDescription: "Installation variant:"; Flags: exclusive
Name: "fd"; Description: "Framework-dependent (requires .NET 9 Desktop Runtime)"; \
  GroupDescription: "Installation variant:"; Flags: exclusive unchecked
; Shortcuts
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
; Startup
Name: "startup"; Description: "Start Krypton &automatically with Windows"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
; Self-contained variant
Source: "..\artifacts\win-x64-sc\*"; DestDir: "{app}"; Tasks: sc; Flags: ignoreversion recursesubdirs createallsubdirs
; Framework-dependent variant
Source: "..\artifacts\win-x64-fd\*"; DestDir: "{app}"; Tasks: fd; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Krypton Clipboard Manager"; Filename: "{app}\Krypton-Desktop.exe"
Name: "{commondesktop}\Krypton Clipboard Manager"; Filename: "{app}\Krypton-Desktop.exe"; Tasks: desktopicon
Name: "{group}\Uninstall Krypton"; Filename: "{uninstallexe}"

[Registry]
; Install metadata for self-updater
Root: HKLM; Subkey: "Software\Krypton"; ValueType: string; ValueName: "InstallType"; ValueData: "setup"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Krypton"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"
Root: HKLM; Subkey: "Software\Krypton"; ValueType: string; ValueName: "Variant"; ValueData: "selfcontained"; Tasks: sc
Root: HKLM; Subkey: "Software\Krypton"; ValueType: string; ValueName: "Variant"; ValueData: "framework"; Tasks: fd
; Startup registry entry (HKCU so it doesn't require admin on run)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "Krypton"; ValueData: """{app}\Krypton-Desktop.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Krypton-Desktop.exe"; Description: "Launch Krypton Clipboard Manager"; Flags: nowait postinstall skipifsilent
