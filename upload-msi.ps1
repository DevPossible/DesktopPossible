#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the signed MSI and portable zip on Windows and attaches them to an existing
    GitHub release.

.DESCRIPTION
    WiX and Authenticode both only run on Windows, so the Linux CI pipeline publishes an
    UNSIGNED portable zip and creates the GitHub release without the MSI. Run this script
    on Windows after the release pipeline has finished: it runs package.ps1 for the
    released version (rebuilding the zip and the MSI from the checked-out commit, both
    Authenticode-signed with Azure Trusted Signing) and uploads them to the GitHub
    release with the gh CLI. The signed zip replaces the unsigned one the pipeline
    attached, so every published artifact is signed.

    Signing requires `az login` as a principal holding the "Artifact Signing Certificate
    Profile Signer" role on the DevPossible signing account.

.PARAMETER Version
    The released version (x.y.z). Defaults to the v-tag at HEAD.

.PARAMETER SkipTests
    Passed through to package.ps1.

.PARAMETER NoSign
    Build and upload unsigned artifacts. Escape hatch for when Trusted Signing is
    unavailable - do not use for a public release.

.EXAMPLE
    git checkout v0.9.0
    ./upload-msi.ps1
#>
param(
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$NoSign
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
    if (-not $NoSign) { $packageArgs.Sign = $true }
    & (Join-Path $PSScriptRoot 'package.ps1') @packageArgs
    if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed" }

    # The zip is uploaded with --clobber to replace the unsigned one the Linux
    # pipeline attached; both are built from this same checked-out commit.
    $assets = @(
        (Join-Path $PSScriptRoot '.dist' "DesktopPossible-$Version-win-x64.msi")
        (Join-Path $PSScriptRoot '.dist' "DesktopPossible-$Version-win-x64.zip")
    )
    foreach ($asset in $assets) {
        if (-not (Test-Path $asset)) { throw "Release asset not found: $asset" }
    }

    Write-Host "`n=== Uploading release assets to v$Version ===" -ForegroundColor Cyan
    & gh release upload "v$Version" @assets --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    foreach ($asset in $assets) {
        Write-Host "  [OK] Attached $(Split-Path $asset -Leaf)" -ForegroundColor Green
    }
    Write-Host "  https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Blue
}
finally {
    Pop-Location
}
