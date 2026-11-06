<#
.SYNOPSIS
  Integrated Strategy System — one-command install on Windows Server or Windows with Docker Desktop (Linux containers). ADR 0157.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\install.ps1
.EXAMPLE
  $env:APP_PORT = 8090; .\deploy\install.ps1          # another port (also POSTGRES_PORT, GOTENBERG_PORT; saved in deploy\.env)
.EXAMPLE
  .\deploy\install.ps1 -Image strategy-app-1.0.0.tar  # offline install: load a saved image instead of building
.NOTES
  Safe to run again: an existing deploy\.env (passwords) is kept.
#>
[CmdletBinding()]
param([string]$Image = '', [switch]$NoBuild)
. (Join-Path $PSScriptRoot 'lib.ps1')

$version = Get-ReleaseVersion
Write-Step "Integrated Strategy System $version - installation"
Assert-Docker

# 1. Settings file with generated passwords (kept when it already exists).
if (-not (Test-Path $script:EnvFile)) {
    Write-Step 'Creating deploy\.env with generated passwords'
    Copy-Item (Join-Path $script:DeployDir '.env.example') $script:EnvFile
    Set-EnvValue 'POSTGRES_PASSWORD' (New-Secret)
    Set-EnvValue 'STRATEGY_APP_PASSWORD' (New-Secret)
    Set-EnvValue 'MEILI_MASTER_KEY' (New-Secret)
    Set-EnvValue 'APP_VERSION' $version
} else {
    Write-Step 'Keeping the existing deploy\.env'
    foreach ($key in 'POSTGRES_PASSWORD', 'STRATEGY_APP_PASSWORD') {
        if (@('', 'change-me', 'change-me-too', 'strategy', 'strategy_app') -contains (Get-EnvValue $key)) {
            Stop-WithError "deploy\.env still has the example value of $key. Set a strong password (or delete deploy\.env to generate new ones on a NEW installation)." 2
        }
    }
}
# 2. Ports: a clear message instead of a failed container start.
$busy = $false
$ports = @(
    @{ Key = 'APP_PORT'; Default = 8080; What = 'the application' },
    @{ Key = 'POSTGRES_PORT'; Default = 5432; What = 'PostgreSQL (loopback only)' },
    @{ Key = 'GOTENBERG_PORT'; Default = 3000; What = 'Gotenberg PDF service (loopback only)' })
foreach ($p in $ports) {
    $port = [int](Get-Setting $p.Key ([string]$p.Default))
    if ((Test-PortInUse $port) -and -not (Test-PortOwnedByInstall $port)) {
        $busy = $true
        Write-Host ''
        Write-Host "  Port $port ($($p.Key), $($p.What)) is already in use on this machine."
        Write-Host '  Choose a free port and run the installer again, for example:'
        Write-Host "      `$env:$($p.Key) = $($port + 10); .\deploy\install.ps1"
        Write-Host "  or set $($p.Key)=<port> in deploy\.env. (Find the program using it: Get-NetTCPConnection -LocalPort $port -State Listen)"
    }
}
if ($busy) { Write-Host ''; Stop-WithError 'Installation stopped: port(s) in use. Nothing was started.' 3 }

# Ports and options given in the environment are saved, so later commands (backup, upgrade) use the same values.
foreach ($key in 'APP_PORT', 'POSTGRES_PORT', 'GOTENBERG_PORT', 'APP_BIND', 'REGISTRY_PREFIX', 'BUILD_CA_FILE') {
    $value = [Environment]::GetEnvironmentVariable($key)
    if ($value) { Set-EnvValue $key $value }
}
if (-not (Get-EnvValue 'APP_VERSION')) { Set-EnvValue 'APP_VERSION' $version }
$env:APP_VERSION = Get-EnvValue 'APP_VERSION'


# 3. Image: load a saved image (offline) or build it from this folder.
if ($Image) {
    Write-Step "Loading the image from $Image"
    & docker load -i $Image
    if ($LASTEXITCODE -ne 0) { Stop-WithError 'docker load failed.' 4 }
} elseif (-not $NoBuild) {
    Write-Step "Building strategy-app:$($env:APP_VERSION) (first build takes several minutes)"
    $revision = ''
    if (Get-Command git -ErrorAction SilentlyContinue) { $revision = (& git -C $script:RootDir rev-parse --short HEAD 2>$null) }
    $env:GIT_REVISION = $revision
    $env:BUILD_CREATED = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    Initialize-BuildCa
    Invoke-Compose build migrate
}

# 4. Start: postgres, gotenberg, migrate (owner role, grants the runtime role), app.
Write-Step 'Starting the services'
Invoke-Compose up -d --no-build
Write-Step 'Waiting for the application to become ready'
if (-not (Wait-App 420)) {
    & docker compose -f $script:ComposeFile --env-file $script:EnvFile --profile app logs --tail 60 migrate app
    Stop-WithError 'The application did not become healthy. See the logs above.' 5
}

$port = Get-Setting 'APP_PORT' '8080'
$hostName = [Net.Dns]::GetHostName()
Write-Host ''
Write-Step "Installed. The application is running at http://$($hostName):$port (put an HTTPS reverse proxy in front of it)."

# 5. First administrator: the setup wizard with the one-time setup token from the application log.
$logs = Invoke-ComposeQuiet logs --no-color app
$tokenLine = @($logs.Output | Where-Object { $_ -like '*Setup token:*' }) | Select-Object -Last 1
Write-Host ''
if ($tokenLine) {
    $token = ($tokenLine -split 'Setup token:', 2)[1].Trim()
    Write-Host "  Open http://$($hostName):$port/setup and enter the one-time setup token:"
    Write-Host "      $token"
    Write-Host '  (Print a new one later: docker compose -f deploy\docker-compose.yml --profile app run --rm app --setup-token)'
} else {
    Write-Host '  No setup token was printed (an administrator already exists, or this build has no setup wizard).'
    Write-Host '  To create the first administrator from the command line (the password is read from standard input):'
    Write-Host "      `$p = Read-Host -AsSecureString 'Administrator password'"
    Write-Host "      [Net.NetworkCredential]::new('', `$p).Password | docker compose -f deploy\docker-compose.yml --profile app run --rm -T app --create-admin --email admin@example.org --password-file /dev/stdin"
}
Write-Host ''
Write-Host '  Back up regularly: .\deploy\backup.ps1   ·   Upgrade: .\deploy\upgrade.ps1   ·   Guide: docs\guides\en\installation.md'
