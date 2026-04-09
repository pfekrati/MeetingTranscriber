; ---------------------------------------------------------------------------
; Inno Setup Script for Meeting Transcriber
;
; This file is consumed by Build-Installer.ps1 which sets #define values
; via the command line. It can also be compiled manually from Inno Setup
; by uncommenting the fallback defaults below.
; ---------------------------------------------------------------------------

; Fallback defaults (uncomment when compiling directly from Inno Setup IDE)
;#define AppVersion "1.0.0"
;#define PublishDir "artifacts\publish"
;#define InstallerDir "artifacts\installer"
;#define AppExeName "MeetingTranscriber.exe"
;#define SignTool ""

[Setup]
AppId={{B8F2A4E7-9C3D-4F1A-8E5B-6D7C0A2F3E9B}
AppName=Meeting Transcriber
AppVersion={#AppVersion}
AppVerName=Meeting Transcriber {#AppVersion}
AppPublisher=Pooyan Fekrati
AppPublisherURL=https://github.com/pfekrati/MeetingTranscriber
AppSupportURL=https://github.com/pfekrati/MeetingTranscriber/issues
DefaultDirName={autopf}\Meeting Transcriber
DefaultGroupName=Meeting Transcriber
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#InstallerDir}
OutputBaseFilename=MeetingTranscriber-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\MeetingTranscriber\Resources\transcript.ico
#if Len(SignTool) > 0
SignTool={#SignTool}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Start Meeting Transcriber with Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Meeting Transcriber"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall Meeting Transcriber"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Meeting Transcriber"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MeetingTranscriber"; ValueType: string; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,Meeting Transcriber}"; Flags: nowait postinstall skipifsilent
