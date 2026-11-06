<#
.SYNOPSIS
  Restore a backup made by backup.ps1 / backup.sh (ADR 0157) and verify it: checksums, exact row counts of every table, audit hash chain.
  REPLACES the current database and files of this installation.
.EXAMPLE
  .\deploy\restore.ps1 -Backup backups\strategy-1.0.0-20261106T020000Z
.EXAMPLE
  $env:APP_VERSION = '1.0.0'; .\deploy\restore.ps1 -Backup <folder> -Yes -WithEnv    # roll back an upgrade / unattended
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Backup, [switch]$Yes, [switch]$WithEnv)
. (Join-Path $PSScriptRoot 'lib.ps1')

$Backup = (Resolve-Path $Backup).Path
if (-not (Test-Path (Join-Path $Backup 'manifest.json')) -or -not (Test-Path (Join-Path $Backup 'SHA256SUMS'))) {
    Stop-WithError "$Backup is not a backup folder (manifest.json / SHA256SUMS missing)." 2
}
Assert-Docker

Write-Step '1/7 Checking the backup''s checksums'
foreach ($line in Get-Content (Join-Path $Backup 'SHA256SUMS') | Where-Object { $_ }) {
    $expected, $name = $line -split '\s+', 2
    $name = $name.TrimStart('*')
    if ((Get-FileSha256 (Join-Path $Backup $name)) -ne $expected.ToLowerInvariant()) {
        Stop-WithError "Checksum mismatch for $($name): the backup is damaged or was changed. Nothing was restored." 7
    }
}
$manifest = Get-Content -Raw (Join-Path $Backup 'manifest.json') | ConvertFrom-Json
if ($WithEnv) {
    Copy-Item (Join-Path $Backup 'env') $script:EnvFile -Force
}
if (-not (Test-Path $script:EnvFile)) { Stop-WithError 'deploy\.env not found. Run .\deploy\install.ps1 first (new machine) or pass -WithEnv.' 2 }
if ($env:APP_VERSION) { Set-EnvValue 'APP_VERSION' $env:APP_VERSION }
$env:APP_VERSION = Get-Setting 'APP_VERSION' (Get-ReleaseVersion)
& docker image inspect "strategy-app:$($env:APP_VERSION)" *> $null
if ($LASTEXITCODE -ne 0) { Stop-WithError "Image strategy-app:$($env:APP_VERSION) is not on this machine. Build it (docker compose -f deploy\docker-compose.yml --profile app build) or load it (docker load -i ...)." 4 }
$db = Get-Setting 'POSTGRES_DB' 'strategy'
$owner = Get-Setting 'POSTGRES_USER' 'strategy'

Write-Host ''
Write-Host "  Backup:   $Backup (version $($manifest.version))"
Write-Host "  Restore into database '$db' of this installation with image strategy-app:$($env:APP_VERSION)."
Write-Host '  The CURRENT database and files are replaced.'
if (-not $Yes) {
    if ((Read-Host '  Type RESTORE to continue') -ne 'RESTORE') { Stop-WithError 'Cancelled; nothing changed.' 1 }
}

Write-Step '2/7 Stopping the application'
Invoke-ComposeQuiet stop app | Out-Null
Invoke-Compose up -d --no-build postgres gotenberg
Wait-Postgres

Write-Step "3/7 Recreating database $db and restoring the dump"
Invoke-Compose exec -T postgres psql -v ON_ERROR_STOP=1 -X -q -U $owner -d postgres -c "DROP DATABASE IF EXISTS `"$db`" WITH (FORCE);" -c "CREATE DATABASE `"$db`" OWNER `"$owner`";"
Invoke-ComposeBinary -Arguments @('exec', '-T', 'postgres', 'pg_restore', '-U', $owner, '-d', $db, '--exit-on-error', '--no-password') -StdInFile (Join-Path $Backup 'database.dump')

Write-Step '4/7 Comparing the row count of every table with the backup'
$expected = @(Get-Content (Join-Path $Backup 'table-counts.tsv') | Where-Object { $_ })
$actual = Get-TableCounts
$diff = Compare-Object -ReferenceObject $expected -DifferenceObject $actual
if (-not $diff) {
    $rows = 0
    foreach ($line in $expected) { $rows += [long]($line.Split("`t")[1]) }
    Write-Host "  $($expected.Count) tables, $rows rows: identical."
} elseif ($manifest.consistent -eq $false) {
    Write-Warn 'Row counts differ (online backup, counts were informative).'
    $diff | Format-Table -AutoSize | Out-String | Write-Host
} else {
    $diff | Format-Table -AutoSize | Out-String | Write-Host
    Stop-WithError 'Row counts differ from the backup: the restore is incomplete. The application was NOT started.' 8
}

Write-Step '5/7 Files'
$archive = Join-Path $Backup 'files.tar.gz'
if (Test-Path $archive) {
    Invoke-ComposeBinary -Arguments @('run', '--rm', '--no-deps', '-T', '--entrypoint', 'sh', 'app', '-c', 'find /var/lib/strategy/files -mindepth 1 -delete && tar xzf - -C /var/lib/strategy/files') -StdInFile $archive
} else {
    Write-Host '  No files archive in the backup (S3 storage): restore the bucket with the storage''s own tools.'
}
$pfx = Join-Path $Backup 'dataprotection.pfx'
if (Test-Path $pfx) {
    $secrets = Join-Path $script:DeployDir 'secrets'
    New-Item -ItemType Directory -Force -Path $secrets | Out-Null
    Copy-Item $pfx (Join-Path $secrets 'dataprotection.pfx') -Force
    Write-Host '  Data Protection certificate restored to deploy\secrets\dataprotection.pfx.'
}

Write-Step '6/7 Migrations (owner role) and runtime-role grants'
Invoke-Compose run --rm --no-deps migrate

Write-Step '7/7 Verifying the audit hash chain'
& docker compose -f $script:ComposeFile --env-file $script:EnvFile --profile app run --rm --no-deps app --verify-audit-chain
if ($LASTEXITCODE -ne 0) { Stop-WithError 'The audit chain of the restored database does not verify. The application was NOT started.' 9 }

Invoke-Compose up -d --no-build app
if (-not (Wait-App 300)) { Stop-WithError 'The application did not become healthy after the restore (docker compose ... logs app).' 5 }
Write-Step 'Restore complete and verified (checksums, row counts, audit chain). The application is running.'
