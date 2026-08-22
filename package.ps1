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

.EXAMPLE
    ./package.ps1
    ./package.ps1 -Version 1.2.3 -SkipTests -NonInteractive
#>
param(
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$NonInteractive,
    [switch]$NoSingleFile,
    [switch]$NoMsi
)

$ErrorActionPreference = 'Stop'

$ProjectName = 'DesktopPossible'
$Runtime = 'win-x64'
$DistDir = Join-Path $PSScriptRoot '.dist'
$BuildDir = Join-Path $PSScriptRoot '.build'

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
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid version '$Version'. Expected format: x.y.z"
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

        $msiPath = Join-Path $DistDir "$ProjectName-$Version-$Runtime.msi"
        $wxs = Join-Path $PSScriptRoot 'installer' 'DesktopPossible.wxs'
        & dotnet wix build $wxs -arch x64 -d "Version=$Version" -d "PublishDir=$publishDir" -d "RepoRoot=$PSScriptRoot" -pdbtype none -o $msiPath
        if ($LASTEXITCODE -ne 0) { throw "MSI build failed with exit code $LASTEXITCODE" }
        Write-Host "  [OK] Created: $msiPath" -ForegroundColor Green
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
