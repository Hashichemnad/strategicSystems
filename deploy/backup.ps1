<#
.SYNOPSIS
  Backup of a Docker installation (ADR 0157): database (incl. the Data Protection key ring), files volume, deploy\.env and the optional
  Data Protection certificate, with a manifest (version, checksums, exact row counts) that restore.ps1 verifies. Same format as backup.sh.
.EXAMPLE
  .\deploy\backup.ps1                          # -> backups\strategy-<version>-<UTC timestamp>\
.EXAMPLE
  .\deploy\backup.ps1 -Output D:\Backups -Online
#>
[CmdletBinding()]
param([string]$Output = '', [switch]$Online, [string]$Certificate = '', [string]$ResultFile = '')
. (Join-Path $PSScriptRoot 'lib.ps1')

Assert-Docker
if (-not (Test-Path $script:EnvFile)) { Stop-WithError 'deploy\.env not found: this folder has no installation to back up.' 2 }
if (-not $Output) { $Output = Join-Path $script:RootDir 'backups' }
$version = Get-Setting 'APP_VERSION' (Get-ReleaseVersion)
$db = Get-Setting 'POSTGRES_DB' 'strategy'
$owner = Get-Setting 'POSTGRES_USER' 'strategy'
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
$out = Join-Path $Output "strategy-$version-$stamp"
New-Item -ItemType Directory -Force -Path $out | Out-Null
# The backup contains passwords (env) and the key ring: only the current user (and administrators) may read it.
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    & icacls $out /inheritance:r /grant:r "$($env:USERNAME):(OI)(CI)F" /grant:r '*S-1-5-32-544:(OI)(CI)F' | Out-Null
} else {
    & chmod 700 $out
}
Write-Step "Backup to $out"

Invoke-ComposeQuiet up -d postgres | Out-Null
Wait-Postgres
$running = Invoke-ComposeQuiet ps -q --status running app
$appWasRunning = [bool]($running.Output | Select-Object -First 1)
$stopped = $false
try {
    if ($appWasRunning -and -not $Online) {
        Write-Step 'Stopping the application for a consistent backup'
        Invoke-Compose stop app
        $stopped = $true
    }

    Write-Step "Database $db (pg_dump custom format, includes core.data_protection_keys)"
    Invoke-ComposeBinary -Arguments @('exec', '-T', 'postgres', 'pg_dump', '-U', $owner, '-d', $db, '-Fc', '--no-password') -StdOutFile (Join-Path $out 'database.dump')
    if ((Get-Item (Join-Path $out 'database.dump')).Length -eq 0) { Stop-WithError 'pg_dump produced an empty file.' 6 }

    Write-Step 'Row counts'
    Write-Utf8Lines (Join-Path $out 'table-counts.tsv') (Get-TableCounts)

    $filesProvider = Get-Setting 'FILES_PROVIDER' 'local'
    if ($filesProvider -eq 'local') {
        Write-Step 'Files volume'
        Invoke-ComposeBinary -Arguments @('run', '--rm', '--no-deps', '-T', '--entrypoint', 'tar', 'app', 'czf', '-', '-C', '/var/lib/strategy/files', '.') -StdOutFile (Join-Path $out 'files.tar.gz')
    } else {
        Write-Warn "FILES_PROVIDER=$($filesProvider): documents are in the S3 bucket; back it up with the storage's own tools."
    }

    Write-Step 'Settings (deploy\.env - contains passwords, keep the backup folder protected)'
    Copy-Item $script:EnvFile (Join-Path $out 'env')
    if (-not $Certificate) {
        $default = Join-Path $script:DeployDir 'secrets\dataprotection.pfx'
        if (Test-Path $default) { $Certificate = $default }
    }
    if ($Certificate) {
        if (-not (Test-Path $Certificate)) { Stop-WithError "Certificate $Certificate not found." 2 }
        Copy-Item $Certificate (Join-Path $out 'dataprotection.pfx')
    }
} finally {
    if ($stopped) {
        Write-Step 'Starting the application again'
        Invoke-ComposeQuiet up -d --no-build --no-deps app | Out-Null
    }
}

$sums = @()
$parts = @()
foreach ($name in 'database.dump', 'files.tar.gz', 'env', 'dataprotection.pfx', 'table-counts.tsv') {
    $path = Join-Path $out $name
    if (Test-Path $path) {
        $hash = Get-FileSha256 $path
        $sums += "$hash  $name"
        $parts += ('    "{0}": {{ "sha256": "{1}", "bytes": {2} }}' -f $name, $hash, (Get-Item $path).Length)
    }
}
Write-Utf8Lines (Join-Path $out 'SHA256SUMS') $sums
$counts = @(Get-Content (Join-Path $out 'table-counts.tsv') | Where-Object { $_ })
$rows = 0
foreach ($line in $counts) { $rows += [long]($line.Split("`t")[1]) }
$consistent = 'true'
if ($Online) { $consistent = 'false' }
$manifest = @(
    '{', '  "format": 1,', '  "product": "strategy",', "  `"version`": `"$version`",",
    "  `"createdUtc`": `"$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))`",",
    "  `"database`": `"$db`",", "  `"filesProvider`": `"$(Get-Setting 'FILES_PROVIDER' 'local')`",", "  `"consistent`": $consistent,",
    '  "parts": {', ($parts -join ",`n"), '  },', "  `"tables`": $($counts.Count),", "  `"rows`": $rows", '}')
Write-Utf8Lines (Join-Path $out 'manifest.json') $manifest
if ($ResultFile) { Write-Utf8Lines $ResultFile @($out) }
Write-Step "Backup complete: $out"
Write-Host "  Copy it to another machine or storage. Restore: .\deploy\restore.ps1 -Backup $out"
