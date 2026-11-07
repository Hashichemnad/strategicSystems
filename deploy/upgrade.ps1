<#
.SYNOPSIS
  In-place upgrade of a Docker installation to the version of this release folder (ADR 0157):
  new image (built while the current version runs) -> pre-upgrade backup -> migrations as the owner + runtime-role grants -> start -> health check.
.EXAMPLE
  .\deploy\upgrade.ps1
.EXAMPLE
  .\deploy\upgrade.ps1 -NoBuild                         # the new image strategy-app:<version> is already loaded
.EXAMPLE
  .\deploy\upgrade.ps1 -Image strategy-app-1.0.1.tar     # offline: load the new image
.NOTES
  Rollback (printed again on failure): $env:APP_VERSION='<previous>'; .\deploy\restore.ps1 -Backup <pre-upgrade backup> -Yes
#>
[CmdletBinding()]
param([string]$Image = '', [switch]$NoBackup, [switch]$NoBuild)
. (Join-Path $PSScriptRoot 'lib.ps1')

Assert-Docker
if (-not (Test-Path $script:EnvFile)) { Stop-WithError 'deploy\.env not found: no installation here. Use .\deploy\install.ps1 for a new installation.' 2 }
$oldVersion = Get-Setting 'APP_VERSION' 'dev'
$newVersion = $env:NEW_VERSION
if (-not $newVersion) { $newVersion = Get-ReleaseVersion }
Write-Step "Upgrade strategy-app $oldVersion -> $newVersion"

$backupDir = ''

function Show-Rollback {
    Write-Host ''
    Write-Host "  The upgrade did not complete. The previous image strategy-app:$oldVersion is still on this machine."
    if ($backupDir) {
        Write-Host '  Roll back (restores the database and files taken just before the upgrade):'
        Write-Host "      `$env:APP_VERSION = '$oldVersion'; .\deploy\restore.ps1 -Backup '$backupDir' -Yes"
    } else {
        Write-Host "  Roll back: `$env:APP_VERSION = '$oldVersion'; .\deploy\restore.ps1 -Backup <your latest backup> -Yes"
    }
    Write-Host '  Logs: docker compose -f deploy\docker-compose.yml --profile app logs migrate app'
}

# 1. New image first, while the current version keeps running (a failed build changes nothing).
try {
    if ($Image) {
        Write-Step 'Loading the new image'
        if ((Invoke-Native -Command 'docker' -Arguments @('load', '-i', $Image)).Code -ne 0) { throw 'docker load failed.' }
    } elseif ($NoBuild) {
        if ((Invoke-Native -Command 'docker' -Arguments @('image', 'inspect', "strategy-app:$newVersion") -Quiet).Code -ne 0) {
            throw "-NoBuild: image strategy-app:$newVersion is not on this machine."
        }
    } else {
        Write-Step "Building strategy-app:$newVersion (the current version keeps running)"
        $env:GIT_REVISION = Get-SourceRevision
        $env:BUILD_CREATED = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
        Initialize-BuildCa
        $env:APP_VERSION = $newVersion
        Invoke-Compose build migrate
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

Write-Step "Starting strategy-app:$newVersion"
Invoke-Compose up -d --no-build
if (-not (Wait-App 420)) {
    Show-Rollback
    Stop-WithError 'The new version did not become healthy.' 5
}
Write-Step "Upgrade to $newVersion complete. Keep the pre-upgrade backup until you have checked the application: $backupDir"
