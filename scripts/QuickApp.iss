; QuickApp Windows installer.
; Build from the repository root with Inno Setup 6 and pass /DAppVersion=x.y.z.

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64\net10.0-windows\QuickApp"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{B3D0D6A4-4F1A-4CB9-9D8C-2EEA8CE2F4A6}
AppName=QuickApp
AppVersion={#AppVersion}
AppPublisher=Dotnet9
AppPublisherURL=https://github.com/dotnet9/QuickApp
AppSupportURL=https://github.com/dotnet9/QuickApp/issues
DefaultDirName={autopf}\QuickApp
DefaultGroupName=QuickApp
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=QuickApp-v{#AppVersion}-win-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
ChangesAssociations=no
CloseApplications=yes
RestartApplications=yes
CloseApplicationsFilter=QuickApp.exe
UninstallDisplayIcon={app}\QuickApp.exe
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\QuickApp"; Filename: "{app}\QuickApp.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\QuickApp"; Filename: "{app}\QuickApp.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\QuickApp.exe"; Description: "Launch QuickApp"; Flags: nowait postinstall skipifsilent
