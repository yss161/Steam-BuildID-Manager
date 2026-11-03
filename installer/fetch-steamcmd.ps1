# Fetches the official SteamCMD bootstrap so the installer can bundle it.
#
# The bundle only ships Valve's unmodified bootstrap (steamcmd.exe plus its shipped
# support files). SteamCMD still performs its own first-run self-update on the user's
# machine, so the bundled copy is never expected to stay current.
#
# Usage:  pwsh -File installer/fetch-steamcmd.ps1
# Output: installer/steamcmd/  (git-ignored)

[CmdletBinding()]
param(
    [string] $Destination,
    [string[]] $Urls = @(
        'https://media.steampowered.com/client/installer/steamcmd.zip',
        'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
    )
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# $PSScriptRoot is not available in parameter defaults, so resolve it here.
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $PSScriptRoot 'steamcmd'
}

$zipPath = Join-Path ([System.IO.Path]::GetTempPath()) ("steamcmd-" + [guid]::NewGuid().ToString('N') + ".zip")
$downloaded = $false

foreach ($url in $Urls) {
    try {
        Write-Host "Downloading $url ..."
        Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing -TimeoutSec 180
        $downloaded = $true
        break
    }
    catch {
        Write-Warning "Failed from $url : $($_.Exception.Message)"
    }
}

if (-not $downloaded) {
    throw 'Could not download steamcmd.zip from any mirror.'
}

if (Test-Path $Destination) {
    Remove-Item -LiteralPath $Destination -Recurse -Force
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

Expand-Archive -LiteralPath $zipPath -DestinationPath $Destination -Force
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Destination 'steamcmd.exe'
if (-not (Test-Path $exe)) {
    throw "steamcmd.exe was not found in the downloaded archive."
}

$info = Get-Item $exe
Write-Host ("Bundled SteamCMD: {0} ({1:N0} bytes, version {2})" -f `
    $info.FullName, $info.Length, $info.VersionInfo.FileVersion)

Get-ChildItem -LiteralPath $Destination -Recurse -File |
    ForEach-Object { Write-Host ("  {0}  {1:N0}" -f $_.Name, $_.Length) }
