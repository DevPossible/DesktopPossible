#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the DesktopPossible solution.

.DESCRIPTION
    Builds src/DesktopPossible.sln with the dotnet CLI. The project targets
    net10.0-windows (WPF + WinForms) but COM interop is late-bound, so it compiles
    on any OS with the .NET 10+ SDK (EnableWindowsTargeting is set in
    Directory.Build.props). The app itself runs on Windows only.

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
$ProjectName = 'DesktopPossible'

Push-Location $PSScriptRoot
try {
    Write-Host "=== DesktopPossible Build ($Configuration) ===" -ForegroundColor Cyan

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw ".NET SDK not found. Install the .NET 10+ SDK: https://dotnet.microsoft.com/download"
    }
    Write-Host "  [OK] dotnet SDK: $(dotnet --version)" -ForegroundColor Green

    # Clean previous build (handle locked files gracefully)
    if ($Clean -and (Test-Path $BuildDir)) {
        try {
            Remove-Item $BuildDir -Recurse -Force -ErrorAction Stop
            Write-Host "  [OK] Cleaned $BuildDir" -ForegroundColor Green
        }
        catch {
            Write-Host "  [WARN] Could not fully clean build directory (files may be locked)" -ForegroundColor Yellow
            Write-Host "  Attempting to kill processes locking files..." -ForegroundColor Yellow

            # Try to kill any running DesktopPossible processes
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

    dotnet build src/DesktopPossible.sln --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

    $exePath = Join-Path $BuildDir $ProjectName 'bin' $Configuration 'net10.0-windows10.0.19041.0' "$ProjectName.exe"
    Write-Host "`n=== Build Complete ===" -ForegroundColor Green
    Write-Host "  Configuration: $Configuration" -ForegroundColor Gray
    Write-Host "  Build outputs: $BuildDir" -ForegroundColor Gray
    if (Test-Path $exePath) {
        Write-Host "  [OK] Executable: $exePath" -ForegroundColor Green
    } elseif ($IsWindows) {
        Write-Host "  [WARN] Expected executable not found: $exePath" -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
