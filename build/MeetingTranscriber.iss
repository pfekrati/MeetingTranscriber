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

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef AppExeName
  #define AppExeName "MeetingTranscriber.exe"
#endif

#define MyAppName "Meeting Transcriber"
#define MyAppPublisher "Pooyan Fekrati"
#define MyAppURL "https://github.com/pfekrati/MeetingTranscriber"
#define MyAppId "{{A921EB93-237E-4F4D-9AF1-3CCFC8D4074B}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=MeetingTranscriber-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\MeetingTranscriber\Resources\transcript.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
