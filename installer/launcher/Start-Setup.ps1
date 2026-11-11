# Launches the Inno Setup installer after pointing TEMP/TMP at a writable folder.
#
# Inno Setup extracts its own files into %TEMP%\is-XXXX.tmp before any [Code] runs, so an
# unwritable temp folder always shows "安装程序无法创建目录 / Error 5: 拒绝访问". Nothing
# inside the .iss can change that. Setup does honour the TEMP and TMP environment
# variables, so this launcher picks a folder that actually works and then starts Setup
# with the normal wizard - the user still sees the standard installation pages.
#
# Candidates are tried in order, each verified by really creating and removing a folder.

[CmdletBinding()]
param(
    [string] $SetupPath,
    [string[]] $SetupArguments,
    [switch] $Wait
)

$ErrorActionPreference = 'Stop'

function Test-WritableFolder {
    param([string] $Path)

    try {
        if (-not (Test-Path -LiteralPath $Path)) {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
        }
        # Inno creates a directory, so prove a directory can be created, not just a file.
        $probe = Join-Path $Path ('~sbim-probe-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $probe -Force | Out-Null
        Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        return $false
    }
}

function Show-Failure {
    param([System.Collections.IDictionary] $Attempts)

    $lines = @(
        '安装程序无法准备临时目录，因此无法启动。',
        '',
        '已尝试以下位置，都无法创建文件夹：'
    )
    foreach ($key in $Attempts.Keys) {
        $lines += ('  · ' + $key + ' —— ' + $Attempts[$key])
    }
    $lines += @(
        '',
        '可以这样处理：',
        '1. 右键“此电脑”→“属性”→“高级系统设置”→“环境变量”，把 TEMP 和 TMP 指向一个可写目录，例如 %USERPROFILE%\Temp；',
        '2. 临时关闭安全软件的“受控文件夹访问”后重试；',
        '3. 或直接使用便携版 zip：解压后运行 Unblock.cmd，再运行 SteamBuildIDManager.exe，不需要安装。'
    )

    $message = $lines -join [Environment]::NewLine
    Write-Host $message -ForegroundColor Red

    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            $message,
            'Steam BuildID 管理器 · 无法启动安装程序',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
    catch {
        Read-Host '按回车键关闭'
    }
}

# ── Locate the installer ─────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($SetupPath)) {
    $candidate = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*Setup*.exe' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'Start-Setup*' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        Write-Host '没有找到安装程序（*Setup*.exe）。请把 Start-Setup.cmd 与安装包放在同一个文件夹。' -ForegroundColor Red
        Read-Host '按回车键关闭'
        exit 1
    }
    $SetupPath = $candidate.FullName
}

if (-not (Test-Path -LiteralPath $SetupPath)) {
    Write-Host "找不到安装程序：$SetupPath" -ForegroundColor Red
    Read-Host '按回车键关闭'
    exit 1
}

# ── Pick a working temp folder ───────────────────────────────────────────
$candidates = [ordered]@{}

# Our own app folder first: the application already writes backups there, so it is the
# most likely place to be writable even when the system temp folder is locked down.
$candidates['%LocalAppData%\SteamBuildIDManager\InstallTemp'] =
    Join-Path $env:LOCALAPPDATA 'SteamBuildIDManager\InstallTemp'

if (-not [string]::IsNullOrWhiteSpace($env:TEMP)) {
    $candidates['系统 TEMP'] = $env:TEMP
}
if (-not [string]::IsNullOrWhiteSpace($env:TMP) -and $env:TMP -ne $env:TEMP) {
    $candidates['系统 TMP'] = $env:TMP
}
$candidates['安装包所在目录'] = Join-Path $PSScriptRoot 'Temp'
$candidates['系统盘根目录'] = Join-Path $env:SystemDrive 'SBIM-Temp'

$attempts = [ordered]@{}
$chosen = $null

foreach ($entry in $candidates.GetEnumerator()) {
    if (Test-WritableFolder $entry.Value) {
        $chosen = $entry.Value
        Write-Host "临时目录：$chosen"
        break
    }
    $attempts[$entry.Key] = $entry.Value + '（无法写入）'
}

if (-not $chosen) {
    Show-Failure $attempts
    exit 2
}

# ── Start Setup with the normal wizard ───────────────────────────────────
$env:TEMP = $chosen
$env:TMP = $chosen

$arguments = @()
if ($SetupArguments) { $arguments = $SetupArguments }

Write-Host "正在启动安装程序：$SetupPath"

try {
    # -Wait is used by the automated smoke test; the normal double-click path returns
    # immediately so this console window does not linger behind the wizard.
    $startArgs = @{ FilePath = $SetupPath }
    if ($arguments.Count -gt 0) { $startArgs['ArgumentList'] = $arguments }
    if ($Wait) { $startArgs['Wait'] = $true; $startArgs['PassThru'] = $true }

    $process = Start-Process @startArgs
    if ($Wait -and $process) { exit $process.ExitCode }
}
catch {
    Write-Host ('启动安装程序失败：' + $_.Exception.Message) -ForegroundColor Red
    Read-Host '按回车键关闭'
    exit 3
}

exit 0
