#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the full test suite.

.DESCRIPTION
    Runs all tests in the solution, then any additional test projects under
    tests/ that are not already part of the solution (so new test projects are
    picked up automatically). Assumes ./build.ps1 has already been run.
#>
$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    # Verify build outputs exist (dotnet build does not work here due to COMReference items)
    $testOutputDir = Join-Path $PSScriptRoot '.build' 'DesktopFramesPossible.Tests' 'bin' 'Release'
    if (-not (Test-Path $testOutputDir)) {
        throw "Build outputs not found at: $testOutputDir`nRun ./build.ps1 first (this project requires full MSBuild; tests run with --no-build)."
    }

    # Run all tests included in the solution
    dotnet test src/DesktopFramesPossible.sln --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }

    # Run any extra test projects from tests/ not already in the solution
    $solutionContent = Get-Content (Join-Path $PSScriptRoot 'src' 'DesktopFramesPossible.sln') -Raw
    $extraTests = Get-ChildItem -Path 'tests' -Recurse -Filter '*.csproj' -ErrorAction SilentlyContinue |
        Where-Object { $solutionContent -notmatch [regex]::Escape($_.Name) }

    foreach ($testProject in $extraTests) {
        Write-Host "Running extra test project: $($testProject.Name)" -ForegroundColor Yellow
        dotnet test $testProject.FullName --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "Tests failed for $($testProject.Name) with exit code $LASTEXITCODE" }
    }

    Write-Host "  [OK] Full test suite passed" -ForegroundColor Green
}
finally {
    Pop-Location
}
