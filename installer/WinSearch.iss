; Compile through scripts/Build-Setup.ps1. Keep AppId stable across upgrades.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\publish\win-x64"
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir SourcePath + "..\artifacts\installer"
#endif
#if !FileExists(PublishDir + "\WinSearch.exe")
  #error "Publish the unpackaged x64 application before compiling Setup."
#endif

[Setup]
AppId={{BE2CFE48-DC09-45DB-B83F-3CD698881473}
AppName=WinSearch
AppVersion={#AppVersion}
AppPublisher=yedmahos
AppPublisherURL=https://github.com/yedmahos/WinSearch
AppSupportURL=https://github.com/yedmahos/WinSearch/issues
DefaultDirName={localappdata}\Programs\WinSearch
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#SetupOutputDir}
OutputBaseFilename=WinSearch-Setup-{#AppVersion}
SetupIconFile=..\Assets\WinSearch.ico
UninstallDisplayIcon={app}\WinSearch.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=BeforeInstall.txt

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; Preserve the entire publish tree, including native runtime DLLs and PRI files.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\WinSearch"; Filename: "{app}\WinSearch.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\WinSearch"; Filename: "{app}\WinSearch.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WinSearch.exe"; Description: "Launch WinSearch"; Flags: nowait postinstall skipifsilent
