[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'dist\AudioOrb-win-x64'
if (-not (Test-Path (Join-Path $source 'AudioOrb.exe'))) {
    throw 'Run scripts\publish.ps1 first.'
}

$target = Join-Path $env:LOCALAPPDATA 'Programs\AudioOrb'
if (Test-Path $target) {
    Get-Process AudioOrb -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Remove-Item $target -Recurse -Force
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$source\*" $target -Recurse -Force

$exe = Join-Path $target 'AudioOrb.exe'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\AudioOrb.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$target\Assets\App.ico"
$shortcut.Description = 'AudioOrb - quick output device switcher'
$shortcut.Save()

Start-Process $exe
Write-Host "Installed to: $target" -ForegroundColor Green
Write-Host "Start menu shortcut: $shortcutPath" -ForegroundColor Green
