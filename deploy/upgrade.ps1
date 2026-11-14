<#
.SYNOPSIS
  In-place upgrade of a Docker installation to the version of this release folder (ADR 0157):
  new image (pulled while the current version runs) -> pre-upgrade backup -> migrations as the owner + runtime-role grants -> start -> health check.
.EXAMPLE
  .\deploy\upgrade.ps1                                 # pull the new release image (ghcr.io, or APP_IMAGE in deploy\.env)
.EXAMPLE
  .\deploy\upgrade.ps1 -Image strategy-app-1.0.2.tar.gz  # offline: load the release archive (checks <file>.sha256 when present)
.EXAMPLE
  .\deploy\upgrade.ps1 -Build                          # build the new image from this source folder (docker-compose.build.yml)
.EXAMPLE
  .\deploy\upgrade.ps1 -NoPull                         # the new image <APP_IMAGE>:<version> is already loaded (-NoBuild: old name)
.NOTES
  Rollback (printed again on failure): $env:APP_VERSION='<previous>'; .\deploy\restore.ps1 -Backup <pre-upgrade backup> -Yes
#>
[CmdletBinding()]
param([string]$Image = '', [switch]$NoBackup, [switch]$Build, [switch]$NoPull, [switch]$NoBuild)
. (Join-Path $PSScriptRoot 'lib.ps1')

if ($NoBuild) {
    Write-Warn '-NoBuild is deprecated: use -NoPull (the image is pulled by default since 1.0.2).'
    $NoPull = [switch]$true
}
if (@(@([bool]$Image, [bool]$Build, [bool]$NoPull) | Where-Object { $_ }).Count -gt 1) { Stop-WithError 'Choose one of -Image, -Build and -NoPull.' 2 }

Assert-Docker
if (-not (Test-Path $script:EnvFile)) { Stop-WithError 'deploy\.env not found: no installation here. Use .\deploy\install.ps1 for a new installation.' 2 }
$oldVersion = Get-Setting 'APP_VERSION' 'dev'
$newVersion = $env:NEW_VERSION
if (-not $newVersion) { $newVersion = Get-ReleaseVersion }
# Installations before 1.0.2 have no APP_IMAGE in deploy\.env (their image was built as strategy-app:<version>): the release default.
if (-not (Get-EnvValue 'APP_IMAGE')) { Set-EnvValue 'APP_IMAGE' (Get-AppImage) }
$env:APP_IMAGE = Get-AppImage
$oldRef = "$($env:APP_IMAGE):$oldVersion"
$newRef = "$($env:APP_IMAGE):$newVersion"
Write-Step "Upgrade strategy-app $oldVersion -> $newVersion"

$backupDir = ''
# The running version's image under the current name (tags a pre-1.0.2 strategy-app:<version>): the pre-upgrade backup and a rollback use it.
if (-not (Confirm-LocalImage $oldRef)) { Write-Warn "The current image $oldRef is not on this machine; the pre-upgrade backup and a rollback will try to pull it." }

function Show-Rollback {
    Write-Host ''
    Write-Host "  The upgrade did not complete. The previous image $oldRef is still on this machine."
    if ($backupDir) {
        Write-Host '  Roll back (restores the database and files taken just before the upgrade):'
        Write-Host "      `$env:APP_VERSION = '$oldVersion'; .\deploy\restore.ps1 -Backup '$backupDir' -Yes"
    } else {
        Write-Host "  Roll back: `$env:APP_VERSION = '$oldVersion'; .\deploy\restore.ps1 -Backup <your latest backup> -Yes"
    }
    Write-Host '  Logs: docker compose -f deploy\docker-compose.yml --profile app logs migrate app'
}

# 1. New image first, while the current version keeps running (a failed pull, load or build changes nothing).
try {
    $env:APP_VERSION = $newVersion
    if ($Image) {
        Import-ImageArchive $Image $newRef
    } elseif ($Build) {
        Write-Step "Building $newRef (the current version keeps running)"
        Invoke-BuildAppImage
    } elseif ($NoPull) {
        if (-not (Confirm-LocalImage $newRef)) { throw "-NoPull: image $newRef is not on this machine." }
    } elseif (-not (Invoke-PullAppImage $newRef)) {
        throw "Upgrade stopped: image $newRef not available."
    }
} catch {
    Stop-WithError "$($_.Exception.Message) The running version is unchanged." 4
}

# 2. Pre-upgrade backup with the current version; the application stays stopped afterwards.
$env:APP_VERSION = $oldVersion
if (-not $NoBackup) {
    Write-Step 'Pre-upgrade backup'
    $result = [IO.Path]::GetTempFileName()
    & (Join-Path $script:DeployDir 'backup.ps1') -ResultFile $result -LeaveStopped
    $backupDir = (Read-Utf8Lines $result | Select-Object -First 1)
    Remove-Item -Force $result
    if (-not $backupDir) {
        Invoke-ComposeQuiet up -d --no-build | Out-Null
        Stop-WithError 'The pre-upgrade backup failed; upgrade stopped, the current version was started again.' 6
    }
}

# 3. Migrations as the owner (and runtime-role grants), then the new version.
$env:APP_VERSION = $newVersion
try {
    Write-Step 'Stopping the application'
    Invoke-ComposeQuiet stop app | Out-Null
    Write-Step 'Applying migrations as the database owner and granting the runtime role'
    Invoke-Compose run --rm migrate
} catch {
    Show-Rollback
    Stop-WithError $_.Exception.Message 7
}
Set-EnvValue 'APP_VERSION' $newVersion

Write-Step "Starting $newRef"
Invoke-Compose up -d --no-build
if (-not (Wait-App 420)) {
    Show-Rollback
    Stop-WithError 'The new version did not become healthy.' 5
}
Write-Step "Upgrade to $newVersion complete. Keep the pre-upgrade backup until you have checked the application: $backupDir"
