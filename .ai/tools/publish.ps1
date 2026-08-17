#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publish the HearthSwing WPF app as a single-file, self-contained executable.
.DESCRIPTION
    Runs dotnet publish on HearthSwing/HearthSwing.csproj (Release, win-x64,
    single-file self-contained, ~140 MB). Output goes to publish/ by default.
.PARAMETER Configuration
    Build configuration: Release (default) or Debug.
.PARAMETER OutputDir
    Output directory for the publish result (default: publish).
.EXAMPLE
    .\.ai\tools\publish.ps1
    .\.ai\tools\publish.ps1 -OutputDir "dist"
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish"
)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

Write-Host "Publishing HearthSwing ($Configuration)..." -ForegroundColor Cyan
dotnet publish HearthSwing/HearthSwing.csproj -c $Configuration -o $OutputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "Publish complete. Output in: $OutputDir" -ForegroundColor Green
}
else {
    Write-Host "Publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
