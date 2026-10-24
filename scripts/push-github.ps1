[CmdletBinding()]
param(
    [string]$Repository = 'AudioOrb',
    [ValidateSet('Public', 'Private')]
    [string]$Visibility = 'Public',
    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$git = 'C:\Program Files\Git\cmd\git.exe'
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $git)) { $git = 'git' }
if (-not (Test-Path $gh)) { $gh = 'gh' }

& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not logged in. Run: gh auth login --hostname github.com --git-protocol https --web'
}

$userJson = & $gh api user --jq '{login: .login, id: .id}'
$user = $userJson | ConvertFrom-Json
$login = $user.login
$id = $user.id
& $git config user.name $login
& $git config user.email "$id+$login@users.noreply.github.com"

$visibilityFlag = if ($Visibility -eq 'Private') { '--private' } else { '--public' }

$remote = & $git remote get-url origin 2>$null
if ($LASTEXITCODE -ne 0) {
    # The local commit was created before GitHub login, so refresh its author
    # now that the real account is known. Only do this while there is no remote.
    $dirty = & $git status --porcelain
    if ([string]::IsNullOrWhiteSpace($dirty)) {
        & $git commit --amend --no-edit --reset-author
        & $git tag -f -a v1.0.0 -m 'AudioOrb v1.0.0'
    }

    & $gh repo create $Repository $visibilityFlag --source . --remote origin --push
    if ($LASTEXITCODE -ne 0) { throw "gh repo create failed ($LASTEXITCODE)." }
} else {
    & $git push -u origin main
    if ($LASTEXITCODE -ne 0) { throw "git push failed ($LASTEXITCODE)." }
}

& $git push origin --tags
if ($LASTEXITCODE -ne 0) { throw "git push --tags failed ($LASTEXITCODE)." }

if (-not $SkipRelease) {
    $notes = Join-Path $root 'docs\RELEASE_NOTES_v1.0.0.md'
    $asset = Join-Path $root 'dist\AudioOrb-win-x64.zip'
    if ((Test-Path $notes) -and (Test-Path $asset)) {
        & $gh release create v1.0.0 $asset --title '声枢 AudioOrb v1.0.0' --notes-file $notes
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)." }
    }
    else {
        Write-Warning 'Release notes or zip not found; skipped GitHub Release.'
    }
}

Write-Host "GitHub repository: $login/$Repository" -ForegroundColor Green
