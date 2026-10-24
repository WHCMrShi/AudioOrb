[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration

$exe = Join-Path $root "src\AudioOrb\bin\x64\$Configuration\net8.0-windows10.0.19041.0\AudioOrb.exe"
if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe"
}

Start-Process $exe
Write-Host "Started: $exe" -ForegroundColor Green
