#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the DesktopFramesPossible solution.

.DESCRIPTION
    Builds src/DesktopFramesPossible.sln using full MSBuild (located via vswhere).
    NOTE: The app project contains COMReference items, so `dotnet build` does NOT
    work - full MSBuild.exe from Visual Studio / Build Tools is required.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).

.PARAMETER Clean
    Remove the .build output directory before building.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Debug -Clean
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$BuildDir = Join-Path $PSScriptRoot '.build'
$ProjectName = 'DesktopFramesPossible'

function Find-MSBuild {
    <#
    .SYNOPSIS
        Locates MSBuild.exe via vswhere. Required because the app uses COMReference
        items which the dotnet CLI cannot build.
    #>
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
              "This project uses COM references and requires full MSBuild.`n" +
              "Install Visual Studio 2022+ (or Build Tools for Visual Studio 2022) with the '.NET desktop development' workload."
    }

    return $msbuild
}

Push-Location $PSScriptRoot
try {
    Write-Host "=== DesktopFramesPossible Build ($Configuration) ===" -ForegroundColor Cyan

    $msbuild = Find-MSBuild
    Write-Host "  [OK] MSBuild: $msbuild" -ForegroundColor Green

    # Clean previous build (handle locked files gracefully)
    if ($Clean -and (Test-Path $BuildDir)) {
        try {
            Remove-Item $BuildDir -Recurse -Force -ErrorAction Stop
            Write-Host "  [OK] Cleaned $BuildDir" -ForegroundColor Green
        }
        catch {
            Write-Host "  [WARN] Could not fully clean build directory (files may be locked)" -ForegroundColor Yellow
            Write-Host "  Attempting to kill processes locking files..." -ForegroundColor Yellow

            # Try to kill any running DesktopFramesPossible processes
            Get-Process -Name $ProjectName -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue

            Start-Sleep -Milliseconds 500

            # Try again
            try {
                Remove-Item $BuildDir -Recurse -Force -ErrorAction Stop
                Write-Host "  [OK] Successfully cleaned after killing processes" -ForegroundColor Green
            }
            catch {
                Write-Host "  [WARN] Could not clean - proceeding with incremental build" -ForegroundColor Yellow
            }
        }
    }

    # Restore and build (dash-style switches; -restore performs NuGet restore first)
    & $msbuild src/DesktopFramesPossible.sln -restore -t:Build -p:Configuration=$Configuration -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

    $exePath = Join-Path $BuildDir $ProjectName 'bin' $Configuration 'net8.0-windows7.0' "$ProjectName.exe"
    Write-Host "`n=== Build Complete ===" -ForegroundColor Green
    Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
    Write-Host "  Build outputs: $BuildDir" -ForegroundColor Gray
    if (Test-Path $exePath) {
        Write-Host "  [OK] Executable: $exePath" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] Expected executable not found: $exePath" -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
