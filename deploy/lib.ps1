# Shared helpers of install.ps1, backup.ps1, restore.ps1 and upgrade.ps1 (dot-sourced). ADR 0157, ADR 0163.
# Compatible with Windows PowerShell 5.1 and PowerShell 7 (Windows Server, Windows with Docker Desktop, Linux). Keep every .ps1 file
# ASCII-only: Windows PowerShell 5.1 reads a script without BOM in the ANSI code page (tools/check-powershell.ps1 enforces it).
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:DeployDir = $PSScriptRoot
$script:RootDir = Split-Path -Parent $script:DeployDir
$script:EnvFile = Join-Path $script:DeployDir '.env'
$script:ComposeFile = Join-Path $script:DeployDir 'docker-compose.yml'
# Override with the build section, only for building from source (-Build, ADR 0219).
$script:ComposeBuildFile = Join-Path $script:DeployDir 'docker-compose.build.yml'
# Release images (ADR 0219): public package on GitHub's container registry; APP_IMAGE in deploy\.env points to a customer mirror.
$script:DefaultAppImage = 'ghcr.io/hashichemnad/strategy-app'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Warn([string]$Message) { Write-Host "Warning: $Message" -ForegroundColor Yellow }
function Stop-WithError([string]$Message, [int]$Code = 1) {
    Write-Host "Error: $Message" -ForegroundColor Red
    exit $Code
}

# Files shared with the .sh twins, compose and the container are UTF-8 without BOM with LF line endings (ADR 0163). Windows PowerShell
# 5.1 reads BOM-less UTF-8 as ANSI with Get-Content and writes UTF-16 / ANSI with Set-Content / Out-File, so these helpers use [IO.File].
# Paths are resolved against the PowerShell location ([IO.File] alone uses the process directory, which Set-Location does not change).
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)
function Get-FullPath([string]$Path) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }
function Read-Utf8Text([string]$Path) { [IO.File]::ReadAllText((Get-FullPath $Path), $script:Utf8NoBom) }
function Read-Utf8Lines([string]$Path) { [IO.File]::ReadAllLines((Get-FullPath $Path), $script:Utf8NoBom) }
function Write-Utf8Lines([string]$Path, [string[]]$Lines) {
    [IO.File]::WriteAllText((Get-FullPath $Path), (($Lines -join "`n") + "`n"), $script:Utf8NoBom)
}

# Runs a native program (docker, git, icacls ...) so that its standard error never stops the script (ADR 0163). Windows PowerShell 5.1
# turns redirected native stderr - docker compose writes its progress there - into ErrorRecords, which end the script under
# ErrorActionPreference 'Stop' (also when only the host's own output is redirected: CI, scheduled task, remote shell). Here the preference
# is 'Continue' for this call only, stderr lines become plain text again and the result depends on the exit code alone.
#   -Capture  return the standard output lines instead of showing them (standard error is still shown)
#   -Quiet    show nothing
# Returns @{ Code = <exit code>; Output = <captured standard output lines> }. Simple function on purpose (see Invoke-Compose).
function Invoke-Native {
    param([string]$Command, [string[]]$Arguments = @(), [switch]$Capture, [switch]$Quiet)
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    if (-not (Get-Command $Command -CommandType Application -ErrorAction SilentlyContinue)) { return @{ Code = 127; Output = @() } }
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $global:LASTEXITCODE = 0
    & $Command @Arguments 2>&1 | ForEach-Object {
        if ($_ -is [Management.Automation.ErrorRecord]) {
            if (-not $Quiet) { Write-Host $_.ToString() }
        } elseif ($Capture) {
            $lines.Add([string]$_)
        } elseif (-not $Quiet) {
            Write-Host ([string]$_)
        }
    }
    return @{ Code = $global:LASTEXITCODE; Output = $lines.ToArray() }
}

function Get-ReleaseVersion { (Read-Utf8Text (Join-Path $script:DeployDir 'VERSION')).Trim() }

# Commit of this release folder for the image label, or '' (ADR 0163): a ZIP release has no .git, git may be missing, and a failing
# rev-parse (broken .git, dubious ownership) only means "no revision". Without .git the program is not run at all, so a release unpacked
# inside another repository never takes that repository's commit.
function Get-SourceRevision {
    if (-not (Test-Path (Join-Path $script:RootDir '.git'))) { return '' }
    $r = Invoke-Native -Command 'git' -Arguments @('-C', $script:RootDir, 'rev-parse', '--short', 'HEAD') -Capture -Quiet
    if ($r.Code -ne 0 -or $r.Output.Count -eq 0) { return '' }
    return ([string]$r.Output[0]).Trim()
}

function Get-EnvValue([string]$Key) {
    if (-not (Test-Path $script:EnvFile)) { return '' }
    $value = ''
    foreach ($line in (Read-Utf8Lines $script:EnvFile)) {
        if ($line -match "^$([regex]::Escape($Key))=(.*)$") { $value = $Matches[1].TrimEnd("`r") }
    }
    return $value
}

function Set-EnvValue([string]$Key, [string]$Value) {
    $lines = @()
    if (Test-Path $script:EnvFile) { $lines = @(Read-Utf8Lines $script:EnvFile) }
    $found = $false
    $out = foreach ($line in $lines) {
        if ($line -match "^$([regex]::Escape($Key))=") { $found = $true; "$Key=$Value" } else { $line }
    }
    if (-not $found) { $out = @($out) + "$Key=$Value" }
    # UTF-8 without BOM and LF line endings: docker compose reads the file on Windows and Linux alike.
    Write-Utf8Lines $script:EnvFile @($out)
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

function Get-ComposeArguments { @('compose', '-f', $script:ComposeFile, '--env-file', $script:EnvFile, '--profile', 'app') }

# Runs docker compose for this installation (output shown) and throws on a non-zero exit code. Simple functions with $args on purpose: an
# advanced function (param block with [Parameter()]) would bind "-d" to the common parameter -Debug instead of passing it to docker.
function Invoke-Compose {
    $arguments = @($args)
    $r = Invoke-Native -Command 'docker' -Arguments (@(Get-ComposeArguments) + $arguments)
    if ($r.Code -ne 0) { throw "docker compose $($arguments -join ' ') failed (exit $($r.Code))." }
}

# Runs docker compose without showing anything; returns @{ Code; Output = standard output lines }.
function Invoke-ComposeQuiet {
    $arguments = @($args)
    return (Invoke-Native -Command 'docker' -Arguments (@(Get-ComposeArguments) + $arguments) -Capture -Quiet)
}

# docker compose with the build override (docker-compose.build.yml): building from source only. Throws on a non-zero exit code.
function Invoke-ComposeBuild {
    $arguments = @($args)
    $all = @('compose', '-f', $script:ComposeFile, '-f', $script:ComposeBuildFile, '--env-file', $script:EnvFile, '--profile', 'app') + $arguments
    $r = Invoke-Native -Command 'docker' -Arguments $all
    if ($r.Code -ne 0) { throw "docker compose $($arguments -join ' ') failed (exit $($r.Code))." }
}

# Image repository of the application (without tag): the environment, deploy\.env, then the release default.
function Get-AppImage { Get-Setting 'APP_IMAGE' $script:DefaultAppImage }

function Test-Image([string]$Ref) { (Invoke-Native -Command 'docker' -Arguments @('image', 'inspect', $Ref) -Quiet).Code -eq 0 }

# True when <image>:<version> is on this machine. Installations before 1.0.2 built the image as strategy-app:<version>: such a local image
# is tagged with the current name, so backups, rollbacks and restores to those versions keep working without a pull.
function Confirm-LocalImage([string]$Ref) {
    if (Test-Image $Ref) { return $true }
    $legacy = 'strategy-app:' + $Ref.Substring($Ref.LastIndexOf(':') + 1)
    if ($legacy -eq $Ref -or -not (Test-Image $legacy)) { return $false }
    Write-Step "Tagging the locally built $legacy as $Ref"
    return ((Invoke-Native -Command 'docker' -Arguments @('tag', $legacy, $Ref) -Quiet).Code -eq 0)
}

# Pulls <image>:<version> for migrate and app ($env:APP_IMAGE / $env:APP_VERSION set by the caller). Returns $false with an explanation
# when the registry cannot be reached and the image is not on this machine either; a local copy of the same tag is used with a warning.
function Invoke-PullAppImage([string]$Ref) {
    Write-Step "Pulling $Ref"
    if ((Invoke-Native -Command 'docker' -Arguments (@(Get-ComposeArguments) + @('pull', 'migrate', 'app'))).Code -eq 0) { return $true }
    if (Confirm-LocalImage $Ref) {
        Write-Warn "Could not pull $Ref; using the copy already on this machine."
        return $true
    }
    Write-Host ''
    Write-Host "  Could not pull $Ref (no connection to the registry, or the image does not exist there)."
    Write-Host '  Offline machine: download strategy-app-<version>.tar.gz and its .sha256 file from the GitHub release on a connected machine,'
    Write-Host '  copy both here and run the script again with -Image <file>. Own registry mirror: set APP_IMAGE in deploy\.env.'
    Write-Host '  Build from this source folder instead: -Build.'
    return $false
}

# Loads an offline image archive (docker save; .tar or .tar.gz) and makes sure <image>:<version> exists afterwards. <archive>.sha256 next
# to the archive (sha256sum format) is verified first: a mismatch refuses the archive before anything is loaded. An archive saved under
# another repository name with the same tag (the ghcr.io name for a mirror, or strategy-app:<version> of a source build) is tagged.
# Throws with the reason on any failure.
function Import-ImageArchive([string]$Archive, [string]$Ref) {
    $version = $Ref.Substring($Ref.LastIndexOf(':') + 1)
    if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw "Image archive $Archive not found." }
    $sumFile = "$Archive.sha256"
    if (Test-Path -LiteralPath $sumFile -PathType Leaf) {
        $line = @(Read-Utf8Lines $sumFile | Where-Object { $_.Trim() }) | Select-Object -First 1
        $expected = ''
        if ($line) { $expected = ((([string]$line).Trim() -split '\s+')[0]).ToLowerInvariant() }
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath (Get-FullPath $Archive)).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            throw "Checksum mismatch: $Archive has SHA-256 $actual but $sumFile expects $expected. Download the archive again; nothing was loaded."
        }
        Write-Step "Checksum verified ($sumFile)"
    } else {
        Write-Warn "No $sumFile next to the archive: its checksum was not verified."
    }
    Write-Step "Loading the image from $Archive"
    $load = Invoke-Native -Command 'docker' -Arguments @('load', '-i', (Get-FullPath $Archive)) -Capture
    $load.Output | ForEach-Object { Write-Host $_ }
    if ($load.Code -ne 0) { throw "docker load failed for $Archive." }
    if (-not (Test-Image $Ref)) {
        foreach ($line in @($load.Output)) {
            $m = [regex]::Match([string]$line, '^Loaded image: (\S*strategy-app:' + [regex]::Escape($version) + ')\s*$')
            if ($m.Success) {
                Write-Step "Tagging $($m.Groups[1].Value) as $Ref"
                if ((Invoke-Native -Command 'docker' -Arguments @('tag', $m.Groups[1].Value, $Ref) -Quiet).Code -ne 0) { throw "docker tag $($m.Groups[1].Value) $Ref failed." }
                break
            }
        }
    }
    if (-not (Test-Image $Ref)) { throw "The archive does not contain $Ref (nor another strategy-app:$version to tag)." }
}

# Builds <APP_IMAGE>:<APP_VERSION> from this source folder with the build override (CA bundle, revision and creation labels).
function Invoke-BuildAppImage {
    $env:GIT_REVISION = Get-SourceRevision
    $env:BUILD_CREATED = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    Initialize-BuildCa
    Invoke-ComposeBuild build migrate
}

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Stop-WithError 'Docker is not installed. Install Docker Desktop (Windows 10/11) or Docker Engine / Mirantis Container Runtime (Windows Server) and run this script again.' 10
    }
    if ((Invoke-Native -Command 'docker' -Arguments @('info') -Quiet).Code -ne 0) {
        Stop-WithError 'Docker is installed but not running. Start Docker Desktop (Linux containers mode) and run this script again.' 10
    }
    if ((Invoke-Native -Command 'docker' -Arguments @('compose', 'version') -Quiet).Code -ne 0) {
        Stop-WithError 'Docker Compose v2 is missing (docker compose). Update Docker Desktop or install the compose plugin.' 10
    }
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
            $inspect = Invoke-Native -Command 'docker' -Arguments @('inspect', '-f', '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}', $id) -Capture -Quiet
            $status = ($inspect.Output | Select-Object -First 1)
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
# PowerShell re-encoding the bytes (PowerShell 5.1 pipes are text). Standard error goes straight to the console (no ErrorRecords).
function Invoke-ComposeBinary {
    param([string[]]$Arguments, [string]$StdOutFile = $null, [string]$StdInFile = $null)
    $psi = New-Object Diagnostics.ProcessStartInfo
    # The docker that PowerShell itself runs (PATH order). A bare 'docker' would let Windows search System32 and the application folder
    # before PATH (CreateProcess), so another docker.exe there could win over the one the user put first on PATH.
    $docker = Get-Command docker -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($docker) { $psi.FileName = $docker.Path } else { $psi.FileName = 'docker' }
    $all = @(Get-ComposeArguments) + $Arguments
    $psi.Arguments = ($all | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = (Get-Location -PSProvider FileSystem).ProviderPath
    $psi.RedirectStandardOutput = [bool]$StdOutFile
    $psi.RedirectStandardInput = [bool]$StdInFile
    if ($StdInFile -and -not $StdOutFile -and $PSVersionTable.PSEdition -ne 'Core') {
        # Windows PowerShell 5.1 (.NET Framework): Process.StandardInput is a StreamWriter that writes the console encoding's preamble
        # (a UTF-8 BOM) as soon as it is created, which would corrupt a dump or an archive. Start-Process hands the file itself to the
        # child as its standard input, byte for byte.
        $child = Start-Process -FilePath $psi.FileName -ArgumentList $psi.Arguments -WorkingDirectory $psi.WorkingDirectory `
            -RedirectStandardInput (Get-FullPath $StdInFile) -NoNewWindow -Wait -PassThru
        if ($child.ExitCode -ne 0) { throw "docker compose $($Arguments -join ' ') failed (exit $($child.ExitCode))." }
        return
    }
    $process = [Diagnostics.Process]::Start($psi)
    if ($StdInFile) {
        # PowerShell 7 (.NET): the raw pipe, closed itself.
        $in = [IO.File]::OpenRead((Get-FullPath $StdInFile))
        $pipe = $process.StandardInput.BaseStream
        try { $in.CopyTo($pipe); $pipe.Flush() } finally { $in.Dispose(); $pipe.Close() }
    }
    if ($StdOutFile) {
        $out = [IO.File]::Create((Get-FullPath $StdOutFile))
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
        [IO.File]::WriteAllText($sqlFile, $script:TableCountsSql, $script:Utf8NoBom)
        Invoke-ComposeBinary -Arguments @('exec', '-T', 'postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-X', '-q', '-U', $user, '-d', $db, '-At', '-F', "`t") -StdInFile $sqlFile -StdOutFile $outFile
        return @(Read-Utf8Lines $outFile | Where-Object { $_ -ne '' })
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
