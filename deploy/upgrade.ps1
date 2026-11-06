<#
.SYNOPSIS
  In-place upgrade of a Docker installation to the version of this release folder (ADR 0157):
  pre-upgrade backup -> new image -> migrations as the owner + runtime-role grants -> start -> health check.
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
if (-not $NoBackup) {
    Write-Step 'Pre-upgrade backup'
    $result = [IO.Path]::GetTempFileName()
    & (Join-Path $script:DeployDir 'backup.ps1') -ResultFile $result
    $backupDir = (Get-Content $result | Select-Object -First 1)
    Remove-Item -Force $result
    if (-not $backupDir) { Stop-WithError 'The pre-upgrade backup failed; upgrade stopped, nothing changed.' 6 }
}

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

$env:APP_VERSION = $newVersion
try {
    if ($Image) {
        Write-Step 'Loading the new image'
        & docker load -i $Image
        if ($LASTEXITCODE -ne 0) { throw 'docker load failed.' }
    } elseif ($NoBuild) {
        & docker image inspect "strategy-app:$newVersion" *> $null
        if ($LASTEXITCODE -ne 0) { throw "-NoBuild: image strategy-app:$newVersion is not on this machine." }
    } else {
        Write-Step "Building strategy-app:$newVersion"
        $env:GIT_REVISION = ''
        if (Get-Command git -ErrorAction SilentlyContinue) { $env:GIT_REVISION = (& git -C $script:RootDir rev-parse --short HEAD 2>$null) }
        $env:BUILD_CREATED = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        Initialize-BuildCa
        Invoke-Compose build migrate
    }
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
