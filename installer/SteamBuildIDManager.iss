; Steam BuildID Manager installer (Inno Setup 6)
; Build with: ISCC.exe installer\SteamBuildIDManager.iss
; The publish folder must exist first (see README).

#define MyAppName "Steam BuildID 管理器"
#define MyAppVersion "1.8.0"
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
VersionInfoVersion=1.8.0.0
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

; The SteamCMD bootstrap ships inside publish\steamcmd and is installed to {app}\steamcmd.
; It is only a read-only template: on first use the app copies it into
; %LocalAppData%\SteamBuildIDManager\steamcmd because SteamCMD rewrites itself on start.
#if !FileExists(AddBackslash(SourcePath) + "..\publish\steamcmd\steamcmd.exe")
  #error 缺少内置 SteamCMD：请先运行 installer\fetch-steamcmd.ps1，再把 steamcmd.exe 复制到 publish\steamcmd
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Remove the "downloaded from the internet" marker so SmartScreen stops warning about the app.
; This only touches the installed files, never a system-wide security setting.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Get-ChildItem -LiteralPath '{app}' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; StatusMsg: "正在解除文件锁定…"

Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent