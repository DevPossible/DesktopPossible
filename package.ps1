#Requires -Version 7.0
<#
.SYNOPSIS
    Packages DesktopPossible for distribution.

.DESCRIPTION
    Resolves the version (conventional commits via scripts/get-version.ps1),
    builds the solution, runs smoke tests, publishes a self-contained win-x64
    build via dotnet publish (COM interop is late-bound, so this works on any OS
    with the .NET SDK), and produces a portable zip, an MSI installer (WiX, via
    the local `wix` dotnet tool) and version.json in .dist/.

.PARAMETER Version
    Explicit version to package (x.y.z). When omitted, the version is resolved
    from conventional commits via scripts/get-version.ps1.

.PARAMETER SkipTests
    Skip running smoke tests.

.PARAMETER NonInteractive
    No prompts; also skips CHANGELOG.md updates (CI handles changelogs itself).

.PARAMETER NoSingleFile
    Publish without single-file packaging. Documented fallback if single-file
    publish misbehaves with COM interop / WPF.

.PARAMETER NoMsi
    Skip building the MSI installer (zip only).

.PARAMETER Sign
    Authenticode-sign the published exe and the MSI with Azure Trusted Signing
    (account DevPossible, certificate profile CodeSigning). Windows only - the
    signing APIs are Win32 - so the Linux CI packaging job never passes it and
    the release artifacts are signed on Windows via upload-msi.ps1. Requires
    `az login` as a principal holding "Artifact Signing Certificate Profile
    Signer" on the signing account.

.PARAMETER SignCredentialType
    Azure credential type handed to the sign CLI (azure-cli, azure-powershell,
    managed-identity, workload-identity). Defaults to azure-cli.

.EXAMPLE
    ./package.ps1
    ./package.ps1 -Version 1.2.3 -SkipTests -NonInteractive
    ./package.ps1 -Sign
#>
param(
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$NonInteractive,
    [switch]$NoSingleFile,
    [switch]$NoMsi,
    [switch]$Sign,
    [ValidateSet('azure-cli', 'azure-powershell', 'managed-identity', 'workload-identity')]
    [string]$SignCredentialType = 'azure-cli'
)

$ErrorActionPreference = 'Stop'

$ProjectName = 'DesktopPossible'
$Runtime = 'win-x64'
$DistDir = Join-Path $PSScriptRoot '.dist'
$BuildDir = Join-Path $PSScriptRoot '.build'

# Azure Trusted Signing. None of these are secrets - access is RBAC on the Azure
# account, not a key in the repo. Region is baked into the endpoint host (eus =
# eastus); it must match the region the signing account was created in.
$SigningEndpoint = 'https://eus.codesigning.azure.net/'
$SigningAccount = 'DevPossible'
$SigningCertificateProfile = 'CodeSigning'
$SigningDescriptionUrl = 'https://github.com/DevPossible/DesktopPossible'

function Get-NextVersion {
    # Use conventional commits to calculate version
    $versionScript = Join-Path $PSScriptRoot 'scripts' 'get-version.ps1'
    if (Test-Path $versionScript) {
        $version = & $versionScript
        if ($version -and $version -match '^\d+\.\d+\.\d+$') {
            return $version
        }
    }

    # Fallback: try to get version from git tags
    $lastTag = git describe --tags --abbrev=0 2>$null
    if ($lastTag) {
        $current = [Version]($lastTag -replace '^v', '')
        return "$($current.Major).$($current.Minor).$($current.Build + 1)"
    }

    return "0.1.0"
}

function Invoke-ArtifactSigning {
    <#
        Authenticode-signs one file with Azure Trusted Signing via the pinned
        `sign` dotnet tool (.config/dotnet-tools.json). The certificate is
        short-lived by design (it rolls every few days), so every signature is
        RFC 3161 timestamped - that is what keeps shipped binaries valid long
        after the signing certificate itself expires.
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not $IsWindows) {
        throw "Signing requires Windows (Authenticode uses Win32 APIs). Run package.ps1 -Sign on Windows, or drop -Sign."
    }

    Write-Host "  Signing $(Split-Path $Path -Leaf) ..." -ForegroundColor Gray
    & dotnet sign code artifact-signing $Path `
        --artifact-signing-endpoint $SigningEndpoint `
        --artifact-signing-account $SigningAccount `
        --artifact-signing-certificate-profile $SigningCertificateProfile `
        --azure-credential-type $SignCredentialType `
        --description $ProjectName `
        --description-url $SigningDescriptionUrl `
        --verbosity Warning
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for '$Path' (exit $LASTEXITCODE). Check `az login` and the 'Artifact Signing Certificate Profile Signer' role on the $SigningAccount account."
    }

    # Verify rather than assume: a zero exit code is not proof the file carries a
    # trusted signature.
    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid') {
        throw "Signature verification failed for '$Path': $($signature.Status) - $($signature.StatusMessage)"
    }
    Write-Host "  [OK] Signed: $(Split-Path $Path -Leaf) ($($signature.SignerCertificate.Subject))" -ForegroundColor Green
}

function Update-Changelog {
    param([string]$NewVersion)

    $changelogPath = Join-Path $PSScriptRoot 'CHANGELOG.md'
    if (-not (Test-Path $changelogPath)) { return }

    $today = Get-Date -Format 'yyyy-MM-dd'
    $content = Get-Content $changelogPath -Raw
    $content = $content -replace '## \[Unreleased\]', "## [Unreleased]`n`n## [$NewVersion] - $today"
    Set-Content $changelogPath $content -NoNewline
    Write-Host "  [OK] Updated CHANGELOG.md with version $NewVersion" -ForegroundColor Green
}

Push-Location $PSScriptRoot
try {
    Write-Host "=== DesktopPossible Packaging ===" -ForegroundColor Cyan

    # Determine version
    if (-not $Version) {
        $Version = Get-NextVersion
        Write-Host "Auto-detected version: $Version" -ForegroundColor Yellow

        if (-not $NonInteractive) {
            $confirm = Read-Host "Use this version? (y/n or enter custom version)"
            if ($confirm -ne 'y' -and $confirm -ne 'Y' -and $confirm -ne '') {
                $Version = $confirm
            }
        }
    }
    # x.y.z, optionally with a prerelease suffix (x.y.z-rc.1) for release-candidate builds.
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.]*)?$') {
        throw "Invalid version '$Version'. Expected format: x.y.z or x.y.z-rc.N"
    }
    Write-Host "Packaging version: $Version" -ForegroundColor Green

    # Clean dist folder
    if (Test-Path $DistDir) {
        Remove-Item $DistDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

    # Run build
    Write-Host "`n=== Building ===" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    # Run tests
    if (-not $SkipTests) {
        Write-Host "`n=== Testing ===" -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'test-smoke.ps1')
        if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
    } else {
        Write-Host "  [SKIP] Tests (-SkipTests)" -ForegroundColor DarkGray
    }

    # Publish (dotnet CLI; COM interop is late-bound so no full MSBuild is needed)
    Write-Host "`n=== Publishing ($Runtime) ===" -ForegroundColor Cyan
    $publishDir = Join-Path $BuildDir 'publish' $Runtime
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $publishArgs = @(
        'publish'
        "src/$ProjectName/$ProjectName.csproj"
        '--configuration', 'Release'
        '--runtime', $Runtime
        '--self-contained', 'true'
        "-p:Version=$Version"
        "-p:PublishDir=$publishDir$([System.IO.Path]::DirectorySeparatorChar)"
    )
    if (-not $NoSingleFile) {
        $publishArgs += @(
            '-p:PublishSingleFile=true'
            '-p:IncludeNativeLibrariesForSelfExtract=true'
        )
    } else {
        Write-Host "  [SKIP] Single-file packaging (-NoSingleFile)" -ForegroundColor DarkGray
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
    Write-Host "  [OK] Published to $publishDir" -ForegroundColor Green

    # Sign the published exe BEFORE archiving and before the MSI build, so the zip
    # and the installer both carry the signed binary.
    if ($Sign) {
        Write-Host "`n=== Signing ($SigningAccount / $SigningCertificateProfile) ===" -ForegroundColor Cyan
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (sign)" }
        Invoke-ArtifactSigning -Path (Join-Path $publishDir "$ProjectName.exe")
    } else {
        Write-Host "  [SKIP] Code signing (no -Sign)" -ForegroundColor DarkGray
    }

    # Create archive
    Write-Host "`n=== Archiving ===" -ForegroundColor Cyan
    $archivePath = Join-Path $DistDir "$ProjectName-$Version-$Runtime.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archivePath -Force
    Write-Host "  [OK] Created: $archivePath" -ForegroundColor Green

    # Build the MSI installer (WiX v4+ authoring in installer/DesktopPossible.wxs).
    # The `wix` dotnet tool is pinned in .config/dotnet-tools.json and runs on any OS.
    if (-not $NoMsi) {
        Write-Host "`n=== Building MSI ===" -ForegroundColor Cyan
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (wix)" }

        # UI + Util extensions (versions must match the pinned wix tool). `extension add`
        # caches into ./.wix and is idempotent; it needs network on first run only.
        $wixExtVersion = (Get-Content (Join-Path $PSScriptRoot '.config' 'dotnet-tools.json') -Raw | ConvertFrom-Json).tools.wix.version
        foreach ($ext in @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')) {
            & dotnet wix extension add "$ext/$wixExtVersion"
            if ($LASTEXITCODE -ne 0) { throw "wix extension add failed for $ext/$wixExtVersion" }
        }

        # MSI ProductVersion must be numeric x.y.z: strip any prerelease suffix. The filename
        # keeps the full version; MajorUpgrade AllowSameVersionUpgrades lets the final x.y.z
        # MSI replace an rc install of the same numeric version.
        $msiVersion = ($Version -split '-')[0]
        $msiPath = Join-Path $DistDir "$ProjectName-$Version-$Runtime.msi"
        $wxs = Join-Path $PSScriptRoot 'installer' 'DesktopPossible.wxs'
        & dotnet wix build $wxs -arch x64 -ext "WixToolset.UI.wixext/$wixExtVersion" -ext "WixToolset.Util.wixext/$wixExtVersion" -d "Version=$msiVersion" -d "PublishDir=$publishDir" -d "RepoRoot=$PSScriptRoot" -pdbtype none -o $msiPath
        if ($LASTEXITCODE -ne 0) { throw "MSI build failed with exit code $LASTEXITCODE" }
        Write-Host "  [OK] Created: $msiPath" -ForegroundColor Green

        # The MSI must be signed after wix build - wix rewrites the package, which
        # would strip any signature applied earlier.
        if ($Sign) { Invoke-ArtifactSigning -Path $msiPath }
    } else {
        Write-Host "  [SKIP] MSI installer (-NoMsi)" -ForegroundColor DarkGray
    }

    # Create version file
    @{
        Version   = $Version
        BuildDate = (Get-Date -Format 'o')
        GitCommit = (git rev-parse HEAD 2>$null)
        GitBranch = (git rev-parse --abbrev-ref HEAD 2>$null)
    } | ConvertTo-Json | Set-Content (Join-Path $DistDir 'version.json')

    # Update changelog (skip in CI - the pipeline handles changelogs itself)
    if (-not $NonInteractive) {
        Update-Changelog -NewVersion $Version
    }

    Write-Host "`n=== Packaging Complete ===" -ForegroundColor Green
    Write-Host "Distribution folder: $DistDir" -ForegroundColor Green
    Write-Host "Version: $Version" -ForegroundColor Green

    # List contents
    Write-Host "`nArtifacts:" -ForegroundColor Cyan
    Get-ChildItem $DistDir -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Replace($DistDir, '.dist')
        $sizeKB = [math]::Round($_.Length / 1KB, 1)
        Write-Host "  $relativePath ($sizeKB KB)" -ForegroundColor Gray
    }
}
finally {
    Pop-Location
}
