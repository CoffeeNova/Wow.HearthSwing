#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build the HearthSwing solution.
.DESCRIPTION
    Runs dotnet build for HearthSwing.slnx. Supports Release/Debug configuration.
.PARAMETER Configuration
    Build configuration: Debug (default) or Release.
.EXAMPLE
    .\.ai\tools\build.ps1
    .\.ai\tools\build.ps1 -Configuration Release
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

Write-Host "Building HearthSwing.slnx ($Configuration)..." -ForegroundColor Cyan
dotnet build HearthSwing.slnx -c $Configuration

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded." -ForegroundColor Green
}
else {
    Write-Host "Build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
