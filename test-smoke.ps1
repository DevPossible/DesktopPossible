#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the unit test suite (fast smoke tests).

.DESCRIPTION
    Runs `dotnet test --no-build` against the solution. Assumes ./build.ps1 has
    already been run (the app uses COM references, so tests cannot build via the
    dotnet CLI - the compiled outputs from MSBuild are required).
#>
$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    # Verify build outputs exist (dotnet build does not work here due to COMReference items)
    $testOutputDir = Join-Path $PSScriptRoot '.build' 'DesktopFramesPossible.Tests' 'bin' 'Release'
    if (-not (Test-Path $testOutputDir)) {
        throw "Build outputs not found at: $testOutputDir`nRun ./build.ps1 first (this project requires full MSBuild; tests run with --no-build)."
    }

    # Run unit tests against prebuilt outputs
    dotnet test src/DesktopFramesPossible.sln --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }

    Write-Host "  [OK] Smoke tests passed" -ForegroundColor Green
}
finally {
    Pop-Location
}
