# Signs the published executable and the installer with an Authenticode certificate.
#
# SmartScreen blocks unsigned executables that have no download reputation. The only
# real fix is a valid code signing certificate; this script wires one into the build so
# it works the same locally and in CI. When no certificate is configured it does nothing
# and reports clearly, so unsigned builds still complete.
#
# Supported inputs (first match wins):
#   1. -PfxPath / SIGNING_PFX_BASE64   PFX file, or base64 of a PFX (CI secret)
#   2. -Thumbprint / SIGNING_THUMBPRINT  Certificate already installed in the store
#
# Usage:
#   pwsh -File installer/sign.ps1 -PfxPath cert.pfx -PfxPassword secret
#   pwsh -File installer/sign.ps1 -Thumbprint ABCD... -Targets publish\SteamBuildIDManager.exe

[CmdletBinding()]
param(
    [string] $PfxPath,
    [string] $PfxPassword,
    [string] $PfxBase64,
    [string] $Thumbprint,
    [string[]] $Targets,
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [string] $Description = 'Steam BuildID Manager'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($TimestampUrl)) { $TimestampUrl = 'http://timestamp.digicert.com' }

function Find-SignTool {
    # Prefer the newest SDK, then anything on PATH.
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $candidate = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    $onPath = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # Last resort: some SDK installs do not use the plain "bin\<version>\x64" layout.
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $found = Get-ChildItem $root -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' } |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

# ── Resolve targets ──────────────────────────────────────────────────────
if (-not $Targets -or $Targets.Count -eq 0) {
    $Targets = @()
    $appExe = Join-Path $PSScriptRoot '..\publish\SteamBuildIDManager.exe'
    if (Test-Path $appExe) { $Targets += (Resolve-Path $appExe).Path }

    $setup = Get-ChildItem (Join-Path $PSScriptRoot '..\artifacts\installer') -Filter '*Setup*.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($setup) { $Targets += $setup.FullName }
}

if ($Targets.Count -eq 0) {
    Write-Host 'Nothing to sign: no published exe or installer was found.'
    exit 0
}

# Never re-sign binaries that already carry a valid publisher signature (e.g. Valve's
# steamcmd.exe). Re-signing someone else's binary is both pointless and misleading.
$Targets = @($Targets | Where-Object {
    if (-not (Test-Path $_)) { return $false }
    $existing = Get-AuthenticodeSignature $_
    if ($existing.Status -eq 'Valid') {
        Write-Host "Skipping (already validly signed): $_"
        return $false
    }
    return $true
})

if ($Targets.Count -eq 0) {
    Write-Host 'All targets are already signed; nothing to do.'
    exit 0
}

# ── Resolve the certificate ──────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($PfxPath) -and -not [string]::IsNullOrWhiteSpace($PfxBase64)) {
    $PfxPath = Join-Path ([System.IO.Path]::GetTempPath()) ("sbim-sign-" + [guid]::NewGuid().ToString('N') + '.pfx')
    [System.IO.File]::WriteAllBytes($PfxPath, [Convert]::FromBase64String($PfxBase64))
    $temporaryPfx = $true
}

if ([string]::IsNullOrWhiteSpace($Thumbprint) -and [string]::IsNullOrWhiteSpace($PfxPath)) {
    Write-Warning 'No signing certificate configured; the build stays unsigned and SmartScreen may block it.'
    Write-Host 'Set SIGNING_PFX_BASE64 + SIGNING_PFX_PASSWORD (or SIGNING_THUMBPRINT) to enable signing.'
    exit 0
}

$signTool = Find-SignTool
if (-not $signTool) {
    throw 'signtool.exe was not found. Install the Windows SDK (or add signtool.exe to PATH).'
}

# ── Sign ─────────────────────────────────────────────────────────────────
try {
    foreach ($target in $Targets) {
        $arguments = @(
            'sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl,
            '/d', $Description
        )
        if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
            $arguments += @('/sha1', $Thumbprint, '/sm')
        }
        else {
            $arguments += @('/f', $PfxPath)
            if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) {
                $arguments += @('/p', $PfxPassword)
            }
        }
        $arguments += $target

        Write-Host "Signing $target ..."
        & $signTool @arguments
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $target with exit code $LASTEXITCODE." }

        $result = Get-AuthenticodeSignature $target
        if ($result.Status -ne 'Valid') {
            throw "Signature verification failed for $target : $($result.Status) $($result.StatusMessage)"
        }
        Write-Host ("  OK: " + $result.SignerCertificate.Subject)
    }
}
finally {
    if ($temporaryPfx -and (Test-Path $PfxPath)) {
        Remove-Item $PfxPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Host 'Signing completed.'
