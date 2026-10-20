#requires -Version 5.1
# Builds all MediaLab apps in Release into Build\<App>\  -  Run from anywhere:  .\build-release.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "Building MediaLab (Release)..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\MediaLab.slnx" -c Release --nologo

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Executables (pin these to the taskbar):" -ForegroundColor Cyan
    Get-ChildItem "$PSScriptRoot\Build" -Recurse -Filter *.exe |
        Where-Object { $_.FullName -notmatch '\\tools\\' } |
        ForEach-Object { Write-Host "  $($_.FullName)" }
    Write-Host ""
    Write-Host "Build succeeded." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Build FAILED - see errors above." -ForegroundColor Red
}

Write-Host ""
if ($host.Name -eq "ConsoleHost") {
    Read-Host "Press Enter to close"
}
