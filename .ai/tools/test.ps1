#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run the HearthSwing test suite.
.DESCRIPTION
    Runs dotnet test for HearthSwing.slnx. Supports an optional NUnit filter.
.PARAMETER Configuration
    Build configuration: Debug (default) or Release.
.PARAMETER Filter
    Optional test filter (e.g., "FullyQualifiedName~CacheProtectorTests").
.EXAMPLE
    .\.ai\tools\test.ps1
    .\.ai\tools\test.ps1 -Configuration Release
    .\.ai\tools\test.ps1 -Filter "FullyQualifiedName~ChangeHistoryServiceTests"
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$Filter = ""
)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

Write-Host "Running tests ($Configuration)..." -ForegroundColor Cyan
if ($Filter) {
    dotnet test HearthSwing.slnx -c $Configuration --filter $Filter
}
else {
    dotnet test HearthSwing.slnx -c $Configuration
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Tests passed." -ForegroundColor Green
}
else {
    Write-Host "Tests failed." -ForegroundColor Red
    exit $LASTEXITCODE
}
