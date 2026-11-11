; Steam BuildID Manager installer (Inno Setup 6)
; Build with: ISCC.exe installer\SteamBuildIDManager.iss
; The publish folder must exist first (see README).

#define MyAppName "Steam BuildID 管理器"
#define MyAppIdName "SteamBuildIDManager"
#define MyAppVersion "1.9.2"
#define MyAppPublisher "yss161"
#define MyAppURL "https://github.com/yss161/Steam-BuildID-Manager"
#define MyAppExeName "SteamBuildIDManager.exe"

[Setup]
AppId={{B7E0B1A4-3F6C-4A1D-9E2B-5C8D7A1F4E60}
; Refuse to install while the app is running instead of overwriting live files.
AppMutex=Local\SteamBuildIDManager_SingleInstance
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 Violet0923, yss161

; Install per user, exactly like the original project. This needs no UAC prompt and
; keeps Setup's temporary folder inside the user profile: an administrative install
; extracts to %WINDIR%\Temp, and on machines where that folder is not writable for
; the elevated token Setup fails with a bare "临时目录没有写入权限" message.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#MyAppIdName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=Steam-BuildID-Manager-Setup-{#MyAppVersion}
SetupIconFile=..\src\SteamBuildIdManager.App\Assets\app.ico
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern windows11
MinVersion=10.0.17763
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
VersionInfoVersion=1.9.2.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

; The SteamCMD bootstrap ships inside publish\steamcmd and lands in {app}\steamcmd.
; On first use the app copies it to %LocalAppData%\SteamBuildIDManager\steamcmd so
; SteamCMD can rewrite its own files there.
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

Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// NOTE: Setup extracts itself into %TEMP%\is-XXXX.tmp before any code here runs, so an
// unwritable temp folder fails with "Error 5: Access is denied" and no [Code] can prevent
// it. Use Start-Setup.cmd (ships next to this installer) to launch Setup with a writable
// TEMP; the user still gets the normal wizard pages.
{ Upgrading an older installation does not remove WinUI resource directories that are
  absent from the new package. Identify them by their MUI payload so user-created
  folders stay untouched. Mirrors the original project's installer. }
procedure RemoveObsoleteLanguageDirectories;
var
  FindRec: TFindRec;
  DirectoryName: String;
  DirectoryPath: String;
  NormalizedName: String;
begin
  if not FindFirst(ExpandConstant('{app}\*'), FindRec) then
    Exit;

  try
    repeat
      DirectoryName := FindRec.Name;
      DirectoryPath := AddBackslash(ExpandConstant('{app}')) + DirectoryName;
      NormalizedName := Lowercase(DirectoryName);

      if (DirectoryName <> '.') and (DirectoryName <> '..') and
         DirExists(DirectoryPath) and
         (NormalizedName <> 'zh-cn') and (NormalizedName <> 'en-us') and
         (FileExists(AddBackslash(DirectoryPath) + 'Microsoft.ui.xaml.dll.mui') or
          FileExists(AddBackslash(DirectoryPath) + 'Microsoft.UI.Xaml.Phone.dll.mui')) then
      begin
        DelTree(DirectoryPath, True, True, True);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveObsoleteLanguageDirectories;
end;
