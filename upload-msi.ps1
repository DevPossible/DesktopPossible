#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the MSI installer on Windows and attaches it to an existing GitHub release.

.DESCRIPTION
    WiX only runs on Windows, so the Linux CI pipeline publishes the portable zip and
    creates the GitHub release without the MSI. Run this script on Windows after the
    release pipeline has finished: it runs package.ps1 for the released version (which
    rebuilds the zip and the MSI from the checked-out commit) and uploads the MSI to the
    GitHub release with the gh CLI.

.PARAMETER Version
    The released version (x.y.z). Defaults to the v-tag at HEAD.

.PARAMETER SkipTests
    Passed through to package.ps1.

.EXAMPLE
    git checkout v0.9.0
    ./upload-msi.ps1
#>
param(
    [string]$Version = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$Repo = 'DevPossible/DesktopPossible'

Push-Location $PSScriptRoot
try {
    if (-not $Version) {
        $tag = git tag --points-at HEAD | Where-Object { $_ -match '^v\d+\.\d+\.\d+(-rc\.\d+)?$' } | Select-Object -First 1
        if (-not $tag) { throw "No release tag at HEAD. Check out the release tag or pass -Version." }
        $Version = $tag.TrimStart('v')
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+(-rc\.\d+)?$') { throw "Invalid version '$Version'. Expected x.y.z or x.y.z-rc.N" }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) is required: winget install GitHub.cli" }

    # Hashtable splatting: an array splat passes elements POSITIONALLY, so the literal
    # string "-Version" bound to package.ps1's $Version parameter and failed validation.
    $packageArgs = @{ Version = $Version; NonInteractive = $true }
    if ($SkipTests) { $packageArgs.SkipTests = $true }
    & (Join-Path $PSScriptRoot 'package.ps1') @packageArgs
    if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed" }

    $msi = Join-Path $PSScriptRoot '.dist' "DesktopPossible-$Version-win-x64.msi"
    if (-not (Test-Path $msi)) { throw "MSI not found: $msi" }

    Write-Host "`n=== Uploading MSI to GitHub release v$Version ===" -ForegroundColor Cyan
    & gh release upload "v$Version" $msi --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    Write-Host "  [OK] Attached $(Split-Path $msi -Leaf) to https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
}
finally {
    Pop-Location
}
