; Steam BuildID Manager installer (Inno Setup 6)
; Build with: ISCC.exe installer\SteamBuildIDManager.iss
; The publish folder must exist first (see README).

#define MyAppName "Steam BuildID 管理器"
#define MyAppVersion "1.3.2"
#define MyAppPublisher "yss161"
#define MyAppURL "https://github.com/yss161/Steam-BuildID-Manager"
#define MyAppExeName "SteamBuildIDManager.exe"

[Setup]
AppId={{B7E0B1A4-3F6C-4A1D-9E2B-5C8D7A1F4E60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 Violet0923, yss161
DefaultDirName={autopf}\SteamBuildIDManager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Always request administrator rights; the app is installed for all users.
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=Steam-BuildID-Manager-Setup-{#MyAppVersion}
SetupIconFile=..\src\SteamBuildIdManager.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
CloseApplicationsFilter=SteamBuildIDManager.exe
RestartApplications=no
LicenseFile=..\LICENSE
VersionInfoVersion=1.3.2.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent