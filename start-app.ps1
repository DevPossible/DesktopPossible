#Requires -Version 7.0
param(
    [switch]$Build  # Trigger a build before running (default: use existing build)
)
$ErrorActionPreference = 'Stop'

# All arguments passed to this script are forwarded to the application
# Usage: ./start-app.ps1                 # Run without building
# Usage: ./start-app.ps1 -Build          # Build first, then run

$ProjectName = 'DesktopPossible'
$Configuration = 'Release'
$Framework = 'net10.0-windows10.0.19041.0'

Push-Location $PSScriptRoot
try {
    # Build only if -Build flag specified
    if ($Build) {
        & ./build.ps1
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    }

    # Find and run the compiled executable
    $exePath = Join-Path $PSScriptRoot ".build/$ProjectName/bin/$Configuration/$Framework/$ProjectName.exe"

    if (-not (Test-Path $exePath)) {
        throw "Executable not found at: $exePath`nRun ./build.ps1 first or use the -Build flag.`nNote: this project requires full MSBuild (VS 2022+ Build Tools with the .NET desktop workload); dotnet build does not work due to COM references."
    }

    # Run the compiled app with all arguments forwarded
    & $exePath @args
}
finally {
    Pop-Location
}
