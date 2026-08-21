#Requires -Version 7.0
<#
.SYNOPSIS
    Packages DesktopFramesPossible for distribution.

.DESCRIPTION
    Resolves the version (conventional commits via scripts/get-version.ps1),
    builds the solution, runs smoke tests, publishes a self-contained win-x64
    build via full MSBuild (COM references prevent use of the dotnet CLI), and
    produces a zip plus version.json in .dist/.

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

.EXAMPLE
    ./package.ps1
    ./package.ps1 -Version 1.2.3 -SkipTests -NonInteractive
#>
param(
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$NonInteractive,
    [switch]$NoSingleFile
)

$ErrorActionPreference = 'Stop'

$ProjectName = 'DesktopFramesPossible'
$Runtime = 'win-x64'
$DistDir = Join-Path $PSScriptRoot '.dist'
$BuildDir = Join-Path $PSScriptRoot '.build'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer' 'vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found at: $vswhere`n" +
              "This project uses COM references and requires full MSBuild.`n" +
              "Install Visual Studio 2022+ (or Build Tools for Visual Studio 2022) with the '.NET desktop development' workload."
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1

    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw "MSBuild.exe not found via vswhere.`n" +
              "Install Visual Studio 2022+ (or Build Tools for Visual Studio 2022) with the '.NET desktop development' workload."
    }

    return $msbuild
}

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
    Write-Host "=== DesktopFramesPossible Packaging ===" -ForegroundColor Cyan

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

    # Locate MSBuild up-front (COM references prevent dotnet publish)
    $msbuild = Find-MSBuild

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

    # Publish (full MSBuild - dotnet publish does not work due to COMReference items)
    Write-Host "`n=== Publishing ($Runtime) ===" -ForegroundColor Cyan
    $publishDir = Join-Path $BuildDir 'publish' $Runtime
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $msbuildArgs = @(
        "src/$ProjectName/$ProjectName.csproj"
        '-restore'
        '-t:Publish'
        '-p:Configuration=Release'
        "-p:RuntimeIdentifier=$Runtime"
        '-p:SelfContained=true'
        "-p:Version=$Version"
        "-p:PublishDir=$publishDir\"
        '-v:minimal'
        '-nologo'
    )
    if (-not $NoSingleFile) {
        $msbuildArgs += @(
            '-p:PublishSingleFile=true'
            '-p:IncludeNativeLibrariesForSelfExtract=true'
        )
    } else {
        Write-Host "  [SKIP] Single-file packaging (-NoSingleFile)" -ForegroundColor DarkGray
    }

    & $msbuild @msbuildArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
    Write-Host "  [OK] Published to $publishDir" -ForegroundColor Green

    # Create archive
    Write-Host "`n=== Archiving ===" -ForegroundColor Cyan
    $archivePath = Join-Path $DistDir "$ProjectName-$Version-$Runtime.zip"
    Compress-Archive -Path "$publishDir\*" -DestinationPath $archivePath -Force
    Write-Host "  [OK] Created: $archivePath" -ForegroundColor Green

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
