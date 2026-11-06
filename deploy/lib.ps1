# Shared helpers of install.ps1, backup.ps1, restore.ps1 and upgrade.ps1 (dot-sourced). ADR 0157.
# Compatible with Windows PowerShell 5.1 and PowerShell 7 (Windows Server, Windows with Docker Desktop, Linux).
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:DeployDir = $PSScriptRoot
$script:RootDir = Split-Path -Parent $script:DeployDir
$script:EnvFile = Join-Path $script:DeployDir '.env'
$script:ComposeFile = Join-Path $script:DeployDir 'docker-compose.yml'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Warn([string]$Message) { Write-Host "Warning: $Message" -ForegroundColor Yellow }
function Stop-WithError([string]$Message, [int]$Code = 1) {
    Write-Host "Error: $Message" -ForegroundColor Red
    exit $Code
}

function Get-ReleaseVersion { (Get-Content -Raw (Join-Path $script:DeployDir 'VERSION')).Trim() }

function Get-EnvValue([string]$Key) {
    if (-not (Test-Path $script:EnvFile)) { return '' }
    $value = ''
    foreach ($line in Get-Content $script:EnvFile) {
        if ($line -match "^$([regex]::Escape($Key))=(.*)$") { $value = $Matches[1].TrimEnd("`r") }
    }
    return $value
}

function Set-EnvValue([string]$Key, [string]$Value) {
    $lines = @()
    if (Test-Path $script:EnvFile) { $lines = @(Get-Content $script:EnvFile) }
    $found = $false
    $out = foreach ($line in $lines) {
        if ($line -match "^$([regex]::Escape($Key))=") { $found = $true; "$Key=$Value" } else { $line }
    }
    if (-not $found) { $out = @($out) + "$Key=$Value" }
    # UTF-8 without BOM and LF line endings: docker compose reads the file on Windows and Linux alike.
    [IO.File]::WriteAllText($script:EnvFile, (($out -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Get-Setting([string]$Key, [string]$Default = '') {
    $value = [Environment]::GetEnvironmentVariable($Key)
    if ([string]::IsNullOrEmpty($value)) { $value = Get-EnvValue $Key }
    if ([string]::IsNullOrEmpty($value)) { $value = $Default }
    return $value
}

function New-Secret {
    $bytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

# Runs docker compose for this installation and throws on a non-zero exit code. Simple functions with $args on purpose: an advanced
# function (param block with [Parameter()]) would bind "-d" to the common parameter -Debug instead of passing it to docker.
function Invoke-Compose {
    $arguments = @($args)
    & docker compose -f $script:ComposeFile --env-file $script:EnvFile --profile app @arguments
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($arguments -join ' ') failed (exit $LASTEXITCODE)." }
}

function Invoke-ComposeQuiet {
    $arguments = @($args)
    $output = & docker compose -f $script:ComposeFile --env-file $script:EnvFile --profile app @arguments 2>$null
    return @{ Code = $LASTEXITCODE; Output = $output }
}

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Stop-WithError 'Docker is not installed. Install Docker Desktop (Windows 10/11) or Docker Engine / Mirantis Container Runtime (Windows Server) and run this script again.' 10
    }
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) { Stop-WithError 'Docker is installed but not running. Start Docker Desktop (Linux containers mode) and run this script again.' 10 }
    & docker compose version *> $null
    if ($LASTEXITCODE -ne 0) { Stop-WithError 'Docker Compose v2 is missing (docker compose). Update Docker Desktop or install the compose plugin.' 10 }
}

function Test-PortInUse([int]$Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync('127.0.0.1', $Port)
        if ($task.Wait(500) -and $client.Connected) { return $true }
    } catch { } finally { $client.Dispose() }
    # A port reserved by Windows (Hyper-V / WinNAT excluded ranges) refuses binding although nothing answers.
    $listener = $null
    try {
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Any, $Port)
        $listener.Start()
        return $false
    } catch { return $true } finally { if ($listener) { $listener.Stop() } }
}

function Test-PortOwnedByInstall([int]$Port) {
    $r = Invoke-ComposeQuiet ps --format '{{.Publishers}}'
    if ($r.Code -ne 0 -or -not $r.Output) { return $false }
    return [bool](($r.Output -join "`n") -match (":" + $Port + '(\]|,| |$)'))
}

function Wait-App([int]$TimeoutSeconds = 300) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $r = Invoke-ComposeQuiet ps -q app
        $id = ($r.Output | Select-Object -First 1)
        if ($id) {
            $status = (& docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $id 2>$null)
            if ($status -eq 'healthy') { return $true }
            if ($status -eq 'exited' -or $status -eq 'dead') { return $false }
        }
        Start-Sleep -Seconds 3
    }
    return $false
}

function Wait-Postgres {
    $user = Get-Setting 'POSTGRES_USER' 'strategy'
    $db = Get-Setting 'POSTGRES_DB' 'strategy'
    for ($i = 0; $i -lt 60; $i++) {
        $r = Invoke-ComposeQuiet exec -T postgres pg_isready -U $user -d $db
        if ($r.Code -eq 0) { return }
        Start-Sleep -Seconds 2
    }
    Stop-WithError 'PostgreSQL did not become ready within 120 s (docker compose logs postgres).' 20
}

# Runs a command whose standard output is binary (pg_dump, tar) into a file, or feeds a file to its standard input, without
# PowerShell re-encoding the bytes (PowerShell 5.1 pipes are text).
function Invoke-ComposeBinary {
    param([string[]]$Arguments, [string]$StdOutFile = $null, [string]$StdInFile = $null)
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = 'docker'
    $all = @('compose', '-f', $script:ComposeFile, '--env-file', $script:EnvFile, '--profile', 'app') + $Arguments
    $psi.Arguments = ($all | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = [bool]$StdOutFile
    $psi.RedirectStandardInput = [bool]$StdInFile
    $process = [Diagnostics.Process]::Start($psi)
    if ($StdInFile) {
        $in = [IO.File]::OpenRead($StdInFile)
        try { $in.CopyTo($process.StandardInput.BaseStream) } finally { $in.Dispose(); $process.StandardInput.Close() }
    }
    if ($StdOutFile) {
        $out = [IO.File]::Create($StdOutFile)
        try { $process.StandardOutput.BaseStream.CopyTo($out) } finally { $out.Dispose() }
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "docker compose $($Arguments -join ' ') failed (exit $($process.ExitCode))." }
}

$script:TableCountsSql = @'
SELECT n.nspname || '.' || c.relname,
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text::bigint
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
  AND NOT (n.nspname = 'scheduling' AND c.relname LIKE 'qrtz_%')
ORDER BY 1;
'@

# "schema.table<TAB>count" lines, identical to lib.sh table_counts (backups made on Linux restore on Windows and back).
function Get-TableCounts {
    $db = Get-Setting 'POSTGRES_DB' 'strategy'
    $user = Get-Setting 'POSTGRES_USER' 'strategy'
    $sqlFile = [IO.Path]::GetTempFileName()
    $outFile = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($sqlFile, $script:TableCountsSql, (New-Object Text.UTF8Encoding($false)))
        Invoke-ComposeBinary -Arguments @('exec', '-T', 'postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-X', '-q', '-U', $user, '-d', $db, '-At', '-F', "`t") -StdInFile $sqlFile -StdOutFile $outFile
        return @(Get-Content $outFile | Where-Object { $_ -ne '' })
    } finally { Remove-Item -Force $sqlFile, $outFile -ErrorAction SilentlyContinue }
}

# Behind a TLS-inspecting proxy the build needs the organisation's CA bundle (BUILD_CA_FILE). Compose reads build secrets inside the
# project folder only, so a bundle elsewhere is copied to deploy\build-ca.local.pem (ignored by version control, public data).
function Initialize-BuildCa {
    $ca = Get-Setting 'BUILD_CA_FILE'
    if (-not $ca -or $ca.StartsWith('./') -or $ca.StartsWith('.\')) { return }
    if (-not (Test-Path $ca)) { Stop-WithError "BUILD_CA_FILE=$ca not found." 2 }
    Copy-Item $ca (Join-Path $script:DeployDir 'build-ca.local.pem') -Force
    Set-EnvValue 'BUILD_CA_FILE' './build-ca.local.pem'
    $env:BUILD_CA_FILE = './build-ca.local.pem'
}

function Get-FileSha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant() }

function Write-Utf8Lines([string]$Path, [string[]]$Lines) {
    [IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}
