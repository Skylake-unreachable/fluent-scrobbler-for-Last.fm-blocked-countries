#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#ifndef AppArch
  #define AppArch ""
#endif
#ifndef SourceDir
  #define SourceDir "publish"
#endif
#ifndef OutputFilename
  #if AppArch != ""
    #define OutputFilename "FluentScrobbler-Setup-" + AppArch
  #else
    #define OutputFilename "FluentScrobbler-Setup"
  #endif
#endif

[Setup]
AppName=FluentScrobbler for Last.fm blocked countries
AppVersion={#MyAppVersion}
AppPublisher=Snow Mint
AppPublisherURL=https://github.com/snw-mint/fluent-scrobbler
AppSupportURL=https://github.com/snw-mint/fluent-scrobbler/issues
AppUpdatesURL=https://github.com/snw-mint/fluent-scrobbler/releases
DefaultDirName={localappdata}\FluentScrobbler
PrivilegesRequired=lowest
DefaultGroupName=FluentScrobbler for Last.fm blocked countries
UninstallDisplayIcon={app}\FluentScrobbler.exe
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=.\Output
OutputBaseFilename={#OutputFilename}
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=FluentScrobbler.exe
SetupIconFile=Assets\AppIcon.ico
LicenseFile=Repo\setup\LICENSE.txt
WizardImageFile=Repo\setup\WizardImageFile.bmp
WizardSmallImageFile=Repo\setup\WizardSmallImageFile.bmp
DisableWelcomePage=no
DisableFinishedPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start FluentScrobbler for Last.fm blocked countries with Windows"; GroupDescription: "Additional options:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{group}\FluentScrobbler for Last.fm blocked countries"; Filename: "{app}\FluentScrobbler.exe"
Name: "{autodesktop}\FluentScrobbler for Last.fm blocked countries"; Filename: "{app}\FluentScrobbler.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FluentScrobbler"; ValueData: """{app}\FluentScrobbler.exe"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\FluentScrobbler.exe"; Description: "{cm:LaunchProgram,FluentScrobbler for Last.fm blocked countries}"; Flags: nowait postinstall skipifsilent
