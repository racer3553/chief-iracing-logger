; Chief Companion — Inno Setup installer script
; Compile with Inno Setup Compiler (Compil32.exe) on the Windows build machine.
; Output: dist\ChiefCompanion-Setup.exe (signable, professional installer).

#define AppName "Chief Companion"
#define AppPublisher "Chief Racing"
#define AppURL "https://chiefracing.com"
#define AppExeName "ChiefCompanion.exe"
#define AppVersion "1.0.0"

[Setup]
AppId={{8B6A3F5C-71D4-4F20-9F8A-F7C2B5A1D9E0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/dashboard/diagnostics
AppUpdatesURL={#AppURL}/dashboard/download
DefaultDirName={autopf}\Chief Racing\Chief Companion
DefaultGroupName=Chief Racing
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=dist
OutputBaseFilename=ChiefCompanion-Setup
SetupIconFile=
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
CloseApplications=force
RestartApplications=no
; Uncomment after you have a code-signing cert configured in Inno Setup tools menu:
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start Chief Companion automatically with Windows"; GroupDescription: "Startup"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked

[Files]
; Single-binary build output from PyInstaller (chief_companion.spec)
Source: "dist\ChiefCompanion.exe"; DestDir: "{app}"; Flags: ignoreversion
; Optional: bundle a README for users
Source: "README_INSTALL.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Chief Companion"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Open Diagnostics";    Filename: "{#AppURL}/dashboard/diagnostics"
Name: "{group}\Open Live Status";    Filename: "{#AppURL}/dashboard/sim-racing/live-status"
Name: "{group}\Uninstall Chief Companion"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Chief Companion"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "ChiefCompanion"; ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Chief Companion now"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean per-user config on uninstall
Type: filesandordirs; Name: "{userappdata}\Chief"
