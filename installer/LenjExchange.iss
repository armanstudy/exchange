; Inno Setup 6 script for سامانه مالی لنج | LENJ Exchange Manager
; Build: run installer\publish.ps1 (publishes to artifacts\publish and compiles this script),
; or publish manually and run:  ISCC.exe installer\LenjExchange.iss
;
; The program goes to Program Files; all business data (database, attachments, backups, logs) lives in
; %LOCALAPPDATA%\LenjExchange and is never touched by setup or uninstall.

#define AppName "LENJ Exchange Manager"
#define AppNameFa "سامانه مالی لنج"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppPublisher "YIWU LENGJI IMPORT AND EXPORT CO., LTD"
#define AppExe "LenjExchange.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{7C1B6E52-4F0A-4C8B-9E1D-3A5F2B8C6D10}
AppName={#AppNameFa} | {#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\LenjExchange
DefaultGroupName={#AppNameFa}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=LenjExchange-Setup-{#AppVersion}
SetupIconFile=..\src\LenjExchange.Desktop\Assets\lenj.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Licenses\*"; DestDir: "{app}\Licenses"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#PublishDir}\LatoFont\*"; DestDir: "{app}\LatoFont"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "..\docs\install-guide.fa.md"; DestDir: "{app}\Docs"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\docs\user-guide.fa.md"; DestDir: "{app}\Docs"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppNameFa}"; Filename: "{app}\{#AppExe}"
Name: "{group}\پوشه داده‌ها (LenjExchange)"; Filename: "{localappdata}\LenjExchange"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppNameFa}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; No [UninstallDelete] for {localappdata}\LenjExchange on purpose: financial data must survive uninstall/reinstall.
