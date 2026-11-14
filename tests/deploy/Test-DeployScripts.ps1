<#
.SYNOPSIS
  Tests of deploy\install.ps1, backup.ps1, restore.ps1 and upgrade.ps1 against a fake docker (ADR 0163). Plain assertions, no Pester:
  the same file runs in Windows PowerShell 5.1 and PowerShell 7, on Windows and Linux.
.DESCRIPTION
  Copies deploy\ into a temporary release folder WITHOUT .git (its path contains non-ASCII characters), puts the fake docker
  (tests\deploy\fake-docker) first on PATH and runs the scripts as child processes of -Shell with their output redirected - the condition
  under which Windows PowerShell 5.1 turned docker's stderr progress into terminating errors. Asserts exit codes, the docker calls made
  and the files written (UTF-8 without BOM, LF line endings, byte-exact binary parts).
.EXAMPLE
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\deploy\Test-DeployScripts.ps1        # scripts under 5.1
.EXAMPLE
  pwsh -NoProfile -File tests/deploy/Test-DeployScripts.ps1 -Shell pwsh                               # scripts under 7
#>
param([string]$Shell = '', [string]$Python = '', [switch]$KeepFiles)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# The release under test (deploy/VERSION) and two later patch versions for the upgrade cases: no test edit on a version bump.
$current = ([IO.File]::ReadAllText((Join-Path (Join-Path $repo 'deploy') 'VERSION'))).Trim()
$parts = $current.Split('.')
$next = '{0}.{1}.{2}' -f $parts[0], $parts[1], ([int]$parts[2] + 1)
$after = '{0}.{1}.{2}' -f $parts[0], $parts[1], ([int]$parts[2] + 2)
$currentRe = [regex]::Escape($current)
$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$utf8 = New-Object Text.UTF8Encoding($false)
$utf8Strict = New-Object Text.UTF8Encoding($false, $true)

if (-not $Shell) { $Shell = (Get-Process -Id $PID).Path }
$shellCommand = Get-Command $Shell -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $shellCommand) { throw "PowerShell '$Shell' not found." }
$Shell = $shellCommand.Source

if (-not $Python) {
    $candidates = @('python3', 'python')
    if ($onWindows) { $candidates = @('python', 'python3') }
    foreach ($c in $candidates) {
        $found = Get-Command $c -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found -and $found.Source -notmatch 'WindowsApps') { $Python = $found.Source; break }
    }
}
if (-not $Python) { throw 'Python 3 is needed for the fake docker (tests/deploy/fake-docker/fake_docker.py).' }

# ---------------------------------------------------------------------------------------------------------------------------------
# Work folder: <temp>\strategy-deploy-tests-<id>\{bin, releases, state}
$work = Join-Path ([IO.Path]::GetTempPath()) ('strategy-deploy-tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$bin = Join-Path $work 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'fake-docker/fake_docker.py') $bin
if ($onWindows) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    $cscOut = & $csc /nologo /out:"$(Join-Path $bin 'docker.exe')" (Join-Path $PSScriptRoot 'fake-docker/DockerShim.cs')
    if ($LASTEXITCODE -ne 0) { $cscOut | Write-Host; throw 'Could not compile the docker.exe forwarder.' }
} else {
    $wrapper = "#!/bin/sh`nexec `"$Python`" -I `"$(Join-Path $bin 'fake_docker.py')`" `"`$@`"`n"
    [IO.File]::WriteAllText((Join-Path $bin 'docker'), $wrapper, $utf8)
    & chmod 755 (Join-Path $bin 'docker')
}

$script:Failures = New-Object 'System.Collections.Generic.List[string]'
$script:Count = 0
function Assert-True([bool]$Condition, [string]$Message) {
    $script:Count++
    if ($Condition) { Write-Host "    ok    $Message" } else { Write-Host "    FAIL  $Message" -ForegroundColor Red; $script:Failures.Add("$($script:Test): $Message") }
}

# Non-ASCII release folder (an Arabic user or folder name is common on the customers' machines).
$nonAscii = [string][char]0x0645 + [char]0x0644 + [char]0x0641 + '-' + [char]0x00E9
function New-Release([string]$Name, [switch]$BrokenGit) {
    $root = Join-Path (Join-Path $work 'releases') "$Name-$nonAscii"
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Recurse (Join-Path $repo 'deploy') (Join-Path $root 'deploy')
    foreach ($f in '.env', 'build-ca.local.pem') { Remove-Item -Force (Join-Path (Join-Path $root 'deploy') $f) -ErrorAction SilentlyContinue }
    if ($BrokenGit) {
        # git is run (".git" exists) and fails with an error on stderr: "not a git repository".
        [IO.File]::WriteAllText((Join-Path $root '.git'), "gitdir: $(Join-Path $work 'no-such-repository')`n", $utf8)
    }
    return $root
}

function Format-Argument([string]$a) {
    if ($a -eq '') { return '""' }
    if ($a -notmatch '[\s"]') { return $a }
    return '"' + ($a -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

# Runs a deploy script in a child PowerShell with stdout / stderr redirected; returns @{ Code; Output; Calls }.
function Invoke-Script([string]$Release, [string]$Script, [string[]]$Arguments = @(), [hashtable]$Environment = @{}) {
    $state = Join-Path $work ('state/' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $state | Out-Null
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Shell
    $all = @('-NoProfile', '-NonInteractive')
    if ($onWindows) { $all += @('-ExecutionPolicy', 'Bypass') }
    $all += @('-File', (Join-Path (Join-Path $Release 'deploy') $Script)) + $Arguments
    $psi.Arguments = ($all | ForEach-Object { Format-Argument $_ }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $Release
    $psi.EnvironmentVariables['PATH'] = $bin + [IO.Path]::PathSeparator + $env:PATH
    $psi.EnvironmentVariables['FAKE_DOCKER_STATE'] = $state
    $psi.EnvironmentVariables['FAKE_DOCKER_PYTHON'] = $Python
    foreach ($k in 'APP_PORT', 'POSTGRES_PORT', 'GOTENBERG_PORT', 'APP_VERSION', 'NEW_VERSION', 'FAKE_DOCKER_FAIL', 'GIT_REVISION', 'APP_IMAGE', 'FAKE_DOCKER_IMAGES', 'FAKE_DOCKER_LOAD_IMAGE') {
        if ($psi.EnvironmentVariables.ContainsKey($k)) { $psi.EnvironmentVariables.Remove($k) }
    }
    # Ports nobody uses on a CI runner or a developer machine (the installer refuses busy ports).
    $psi.EnvironmentVariables['APP_PORT'] = '18431'
    $psi.EnvironmentVariables['POSTGRES_PORT'] = '15431'
    $psi.EnvironmentVariables['GOTENBERG_PORT'] = '13431'
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    $process = [Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) { $process.Kill(); throw "$Script did not finish within 10 minutes." }
    $process.WaitForExit()
    $output = $stdout.Result + $stderr.Result
    $calls = @()
    $log = Join-Path $state 'calls.jsonl'
    if (Test-Path $log) { $calls = @([IO.File]::ReadAllLines($log, $utf8) | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json }) }
    return @{ Code = $process.ExitCode; Output = $output; Calls = $calls; State = $state }
}

function Get-Cmds($Result) { @($Result.Calls | ForEach-Object { $_.cmd }) }
function Find-Call($Result, [string]$Cmd) { @($Result.Calls | Where-Object { $_.cmd -eq $Cmd }) }
# Compose files (-f base names, space-separated) of the first call with this command line.
function Get-CallFiles($Result, [string]$Cmd) {
    $c = @(Find-Call $Result $Cmd)
    if ($c.Count -eq 0) { return '<no call>' }
    return (@($c[0].files) -join ' ')
}
function Test-NoCallLike($Result, [string]$Pattern) { -not (@(Get-Cmds $Result | Where-Object { $_ -match $Pattern }).Count -gt 0) }
function Test-Before($Result, [string]$First, [string]$Second) {
    $cmds = @(Get-Cmds $Result)
    $a = [Array]::IndexOf($cmds, $First); $b = [Array]::IndexOf($cmds, $Second)
    return ($a -ge 0 -and $b -ge 0 -and $a -lt $b)
}
function Get-FirstDump($Result) { @(Get-Cmds $Result | Where-Object { $_ -like 'compose exec -T postgres pg_dump *' }) | Select-Object -First 1 }

# Offline release archives (contents do not matter to the fake docker) with sha256sum-format checksum files, as release.yml publishes them.
$ghcr = 'ghcr.io/hashichemnad/strategy-app'
function New-Archive([string]$Version, [switch]$BadChecksum) {
    $dir = Join-Path $work "archives-$nonAscii"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $file = Join-Path $dir "strategy-app-$Version.tar.gz"
    [IO.File]::WriteAllBytes($file, [byte[]](@(0x1f, 0x8b) + [Text.Encoding]::ASCII.GetBytes("fake image archive $Version`n")))
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
    if ($BadChecksum) { $hash = '0' * 64 }
    [IO.File]::WriteAllText("$file.sha256", "$hash  strategy-app-$Version.tar.gz`n", $utf8)
    return $file
}
$noPullOrBuild = '^compose (pull|build)( |$)'
function Show-OnFailure($Result) {
    if ($script:Failures.Count -gt $script:ShownFailures) {
        Write-Host '    --- script output ---'
        Write-Host $Result.Output
        Write-Host '    --- docker calls ---'
        Get-Cmds $Result | ForEach-Object { Write-Host "    $_" }
        $script:ShownFailures = $script:Failures.Count
    }
}
$script:ShownFailures = 0

# UTF-8 without BOM, LF line endings, valid UTF-8 (what the .sh twins, compose and the container read).
function Test-Utf8File([string]$Path) {
    if (-not (Test-Path $Path)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return $false }
    if ($bytes.Length -ge 2 -and (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) { return $false }
    if ([Array]::IndexOf($bytes, [byte]13) -ge 0) { return $false }
    try { [void]$utf8Strict.GetString($bytes) } catch { return $false }
    return $true
}
function Test-SameBytes([string]$A, [string]$B) {
    if (-not (Test-Path $A) -or -not (Test-Path $B)) { return $false }
    $x = [IO.File]::ReadAllBytes($A); $y = [IO.File]::ReadAllBytes($B)
    if ($x.Length -ne $y.Length) { Write-Host "    sizes differ: $(Split-Path -Leaf $A) $($x.Length) bytes, $(Split-Path -Leaf $B) $($y.Length) bytes"; return $false }
    for ($i = 0; $i -lt $x.Length; $i++) { if ($x[$i] -ne $y[$i]) { return $false } }
    return $true
}
function Get-EnvLine([string]$Release, [string]$Key) {
    $value = $null
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $Release 'deploy/.env'), $utf8)) {
        if ($line.StartsWith("$Key=")) { $value = $line.Substring($Key.Length + 1) }
    }
    return $value
}
function Test-NoNativeErrors($Result) {
    # Windows PowerShell 5.1 shows wrapped stderr as "NativeCommandError" / "RemoteException".
    return -not ($Result.Output -match 'NativeCommandError|RemoteException|CategoryInfo')
}

$psVersion = & $Shell -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'
Write-Host "Deploy script tests: scripts run by $Shell (PowerShell $psVersion), fake docker via $Python"
Write-Host "Work folder: $work"

try {
    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install (fresh, release folder without .git)'
    Write-Host "  $($script:Test)"
    $release = New-Release 'main'
    $r = Invoke-Script $release 'install.ps1'
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    $cmds = @(Get-Cmds $r)
    Assert-True ($cmds -contains 'info') 'docker info checked'
    Assert-True ($cmds -contains 'compose version') 'docker compose version checked'
    $pull = @(Find-Call $r 'compose pull migrate app')
    Assert-True ($pull.Count -eq 1) 'release image pulled by default (compose pull migrate app)'
    if ($pull.Count -eq 1) {
        Assert-True ($pull[0].env.APP_VERSION -eq $current) 'APP_VERSION from deploy/VERSION'
        Assert-True ($pull[0].env.APP_IMAGE -eq $ghcr) 'APP_IMAGE = ghcr.io release package'
    }
    Assert-True ((Get-CallFiles $r 'compose pull migrate app') -eq 'docker-compose.yml') 'pulled without the build override'
    Assert-True (Test-NoCallLike $r '^compose build( |$)') 'nothing built'
    Assert-True ($cmds -contains 'compose up -d --no-build') 'services started'
    Assert-True (Test-Before $r 'compose pull migrate app' 'compose up -d --no-build') 'pull before start'
    $envFile = Join-Path $release 'deploy/.env'
    Assert-True (Test-Utf8File $envFile) '.env is UTF-8 without BOM with LF line endings'
    Assert-True ((Get-EnvLine $release 'POSTGRES_PASSWORD') -match '^[0-9a-f]{48}$') 'generated POSTGRES_PASSWORD'
    Assert-True ((Get-EnvLine $release 'STRATEGY_APP_PASSWORD') -match '^[0-9a-f]{48}$') 'generated STRATEGY_APP_PASSWORD'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq $current) 'APP_VERSION saved'
    Assert-True ((Get-EnvLine $release 'APP_PORT') -eq '18431') 'APP_PORT from the environment saved'
    Assert-True ((Get-EnvLine $release 'APP_IMAGE') -eq $ghcr) 'APP_IMAGE saved'
    $example = [IO.File]::ReadAllLines((Join-Path $release 'deploy/.env.example'), $utf8)
    $written = [IO.File]::ReadAllLines($envFile, $utf8)
    $changed = @($example | Where-Object { $_ -notmatch '^(POSTGRES_PASSWORD|STRATEGY_APP_PASSWORD|MEILI_MASTER_KEY|APP_VERSION|APP_PORT|POSTGRES_PORT|GOTENBERG_PORT)=' -and $written -notcontains $_ })
    Assert-True ($changed.Count -eq 0) "every other line of .env.example kept unchanged, non-ASCII included ($($changed.Count) changed)"
    Assert-True (@($example | Where-Object { $_ -match '[^\x00-\x7F]' }).Count -gt 0) '.env.example has a non-ASCII line (encoding test is meaningful)'
    Assert-True ($r.Output -match 'fake-setup-token-0163') 'setup token printed'
    Assert-True ($r.Output -match 'Installed\. The application is running at http://') 'success message unchanged'
    Show-OnFailure $r
    $password = Get-EnvLine $release 'POSTGRES_PASSWORD'

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install again (keeps .env)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'install.ps1'
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ($r.Output -match 'Keeping the existing deploy\\\.env') 'existing .env kept'
    Assert-True ((Get-EnvLine $release 'POSTGRES_PASSWORD') -eq $password) 'password unchanged'
    Assert-True (Test-Utf8File $envFile) '.env still UTF-8 without BOM, LF'
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'backup'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'backup.ps1'
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    $backups = @(Get-ChildItem -Directory (Join-Path $release 'backups') -ErrorAction SilentlyContinue)
    Assert-True ($backups.Count -eq 1) 'one backup folder'
    $cmds = @(Get-Cmds $r)
    Assert-True ($cmds -contains 'compose stop app') 'application stopped for a consistent backup'
    Assert-True ($cmds -contains 'compose up -d --no-build --no-deps app') 'application started again'
    if ($backups.Count -eq 1) {
        $b = $backups[0].FullName
        Assert-True ($backups[0].Name -match "^strategy-$currentRe-\d{8}T\d{6}Z`$") "folder name strategy-<version>-<UTC> ($($backups[0].Name))"
        Assert-True (Test-SameBytes (Join-Path $b 'database.dump') (Join-Path $r.State 'emitted-pg_dump.bin')) 'database.dump byte-identical to pg_dump output'
        Assert-True (Test-SameBytes (Join-Path $b 'files.tar.gz') (Join-Path $r.State 'emitted-files.tar.gz')) 'files.tar.gz byte-identical to tar output'
        Assert-True (Test-SameBytes (Join-Path $b 'env') $envFile) 'env copied'
        foreach ($f in 'SHA256SUMS', 'manifest.json', 'table-counts.tsv') { Assert-True (Test-Utf8File (Join-Path $b $f)) "$f is UTF-8 without BOM, LF" }
        $counts = ''
        if (Test-Path (Join-Path $b 'table-counts.tsv')) { $counts = [IO.File]::ReadAllText((Join-Path $b 'table-counts.tsv'), $utf8) }
        Assert-True ($counts -eq "core.users`t3`nkpi.kpi`t5`n") 'table counts as returned by psql'
        $sums = @()
        if (Test-Path (Join-Path $b 'SHA256SUMS')) { $sums = @([IO.File]::ReadAllLines((Join-Path $b 'SHA256SUMS'), $utf8) | Where-Object { $_ }) }
        Assert-True ($sums.Count -eq 4) "SHA256SUMS lists 4 parts ($($sums.Count))"
        $bad = 0
        foreach ($line in $sums) {
            if ($line -notmatch '^([0-9a-f]{64})  (\S+)$') { $bad++; continue }
            if ((Get-FileHash -Algorithm SHA256 (Join-Path $b $Matches[2])).Hash.ToLowerInvariant() -ne $Matches[1]) { $bad++ }
        }
        Assert-True ($bad -eq 0) 'SHA256SUMS lines in sha256sum format and correct'
        $manifest = $null
        if (Test-Path (Join-Path $b 'manifest.json')) { $manifest = [IO.File]::ReadAllText((Join-Path $b 'manifest.json'), $utf8) | ConvertFrom-Json }
        Assert-True ($null -ne $manifest -and $manifest.version -eq $current -and $manifest.format -eq 1 -and $manifest.consistent -eq $true) 'manifest version, format, consistent'
        Assert-True ($null -ne $manifest -and $manifest.tables -eq 2 -and $manifest.rows -eq 8) 'manifest tables 2, rows 8'
    }
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'restore (verified)'
    Write-Host "  $($script:Test)"
    if ($backups.Count -eq 1) {
        $r = Invoke-Script $release 'restore.ps1' @('-Backup', $backups[0].FullName, '-Yes')
        Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
        Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
        Assert-True (Test-SameBytes (Join-Path $r.State 'restored.dump') (Join-Path $backups[0].FullName 'database.dump')) 'pg_restore received the dump byte-identical'
        Assert-True (Test-SameBytes (Join-Path $r.State 'restored-files.tar.gz') (Join-Path $backups[0].FullName 'files.tar.gz')) 'files restore received the archive byte-identical'
        $sql = ''
        if (Test-Path (Join-Path $r.State 'psql.sql')) { $sql = [IO.File]::ReadAllText((Join-Path $r.State 'psql.sql'), $utf8) }
        Assert-True ($sql.Contains('DROP DATABASE IF EXISTS "strategy" WITH (FORCE);') -and $sql.Contains('CREATE DATABASE "strategy" OWNER "strategy";')) 'database recreated (SQL with double quotes on stdin)'
        $cmds = @(Get-Cmds $r)
        Assert-True ($cmds -contains 'compose run --rm --no-deps migrate') 'migrations run'
        Assert-True ($cmds -contains 'compose run --rm --no-deps app --verify-audit-chain') 'audit chain verified'
        Assert-True ($cmds[$cmds.Count - 1] -eq 'inspect -f {{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}} fakeappcontainer0163') 'waited for the health check last'
        Assert-True ($r.Output -match 'Restore complete and verified \(checksums, row counts, audit chain\)') 'success message unchanged'
        Show-OnFailure $r

        $script:Test = 'restore of a damaged backup'
        Write-Host "  $($script:Test)"
        $damaged = Join-Path $work "damaged-$nonAscii"
        Copy-Item -Recurse $backups[0].FullName $damaged
        [IO.File]::AppendAllText((Join-Path $damaged 'env'), "TAMPERED=1`n", $utf8)
        $r = Invoke-Script $release 'restore.ps1' @('-Backup', $damaged, '-Yes')
        Assert-True ($r.Code -eq 7) "exit code 7 (was $($r.Code))"
        Assert-True (-not ((Get-Cmds $r) -contains 'compose stop app')) 'nothing stopped or restored'
        Show-OnFailure $r
    }

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = "upgrade to $next (release folder without .git)"
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'upgrade.ps1' @() @{ NEW_VERSION = $next }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    $cmds = @(Get-Cmds $r)
    $pull = @(Find-Call $r 'compose pull migrate app')
    Assert-True ($pull.Count -eq 1 -and $pull[0].env.APP_VERSION -eq $next -and $pull[0].env.APP_IMAGE -eq $ghcr) "new image pulled as $($ghcr):$next"
    Assert-True (Test-NoCallLike $r '^compose build( |$)') 'nothing built'
    Assert-True (Test-Before $r 'compose pull migrate app' (Get-FirstDump $r)) 'pre-upgrade backup after the pull'
    Assert-True ($cmds -contains 'compose run --rm migrate') 'migrations as the owner'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq $next) "APP_VERSION=$next saved"
    Assert-True (Test-Utf8File $envFile) '.env still UTF-8 without BOM, LF'
    Assert-True (@(Get-ChildItem -Directory (Join-Path $release 'backups')).Count -eq 2) 'pre-upgrade backup written'
    Assert-True ($r.Output -match "Upgrade to $([regex]::Escape($next)) complete") 'success message unchanged'
    # (The child's console encoding decides how the non-ASCII part of the path is shown; the file round trip itself is UTF-8.)
    Assert-True ($r.Output -match "checked the application: .*strategy-$currentRe-\d{8}T\d{6}Z") 'pre-upgrade backup folder handed over from backup.ps1'
    Show-OnFailure $r

    $script:Test = 'upgrade with failing migrations (-NoBuild, deprecated alias of -NoPull)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'upgrade.ps1' @('-NoBuild') @{ NEW_VERSION = $after; FAKE_DOCKER_FAIL = 'compose run --rm migrate=3' }
    Assert-True ($r.Code -eq 7) "exit code 7 (was $($r.Code))"
    Assert-True ($r.Output -match '-NoBuild is deprecated: use -NoPull') 'deprecation warning'
    Assert-True (Test-NoCallLike $r $noPullOrBuild) 'nothing pulled or built'
    Assert-True ($r.Output -match 'restore\.ps1 -Backup') 'rollback command printed'
    Assert-True ($r.Output -match "previous image $([regex]::Escape("$($ghcr):$next")) is still on this machine") 'previous image named'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq $next) 'APP_VERSION unchanged'
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install -Build (from source, build override)'
    Write-Host "  $($script:Test)"
    $source = New-Release 'source'
    $r = Invoke-Script $source 'install.ps1' @('-Build')
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    $build = @(Find-Call $r 'compose build migrate')
    Assert-True ($build.Count -eq 1) 'image built once (compose build migrate)'
    Assert-True ((Get-CallFiles $r 'compose build migrate') -eq 'docker-compose.yml docker-compose.build.yml') 'built with docker-compose.build.yml'
    if ($build.Count -eq 1) {
        Assert-True ($build[0].env.GIT_REVISION -eq '') 'no .git: GIT_REVISION empty'
        Assert-True ($build[0].env.APP_VERSION -eq $current) 'APP_VERSION from deploy/VERSION'
        Assert-True ($build[0].env.APP_IMAGE -eq $ghcr) 'built as the release image name'
        # PowerShell 7's ConvertFrom-Json turns an ISO 8601 string into a DateTime (Kind Utc for a trailing Z); 5.1 keeps the string.
        $created = $build[0].env.BUILD_CREATED
        if ($created -is [datetime]) { $createdOk = $created.Kind -eq [DateTimeKind]::Utc -and $created.Millisecond -eq 0 }
        else { $createdOk = [string]$created -match '^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$' }
        Assert-True $createdOk "BUILD_CREATED in UTC ($created)"
    }
    Assert-True (Test-NoCallLike $r '^compose pull( |$)') 'nothing pulled'
    Assert-True (Test-Before $r 'compose build migrate' 'compose up -d --no-build') 'build before start'
    Show-OnFailure $r

    $script:Test = 'upgrade -Build'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $source 'upgrade.ps1' @('-Build', '-NoBackup') @{ NEW_VERSION = $next }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    $build = @(Find-Call $r 'compose build migrate')
    Assert-True ($build.Count -eq 1 -and $build[0].env.APP_VERSION -eq $next) "new image built as $next"
    Assert-True ((Get-CallFiles $r 'compose build migrate') -eq 'docker-compose.yml docker-compose.build.yml') 'built with docker-compose.build.yml'
    Assert-True (Test-NoCallLike $r '^compose pull( |$)') 'nothing pulled'
    Assert-True ((Get-EnvLine $source 'APP_VERSION') -eq $next) "APP_VERSION=$next saved"
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install -Image <archive>.tar.gz (checksum verified, ghcr.io name inside)'
    Write-Host "  $($script:Test)"
    $offline = New-Release 'offline'
    $archive = New-Archive $current
    $r = Invoke-Script $offline 'install.ps1' @('-Image', $archive) @{ FAKE_DOCKER_IMAGES = 'none'; FAKE_DOCKER_LOAD_IMAGE = "$($ghcr):$current" }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    Assert-True ($r.Output -match 'Checksum verified') 'checksum verified'
    Assert-True ((Get-Cmds $r) -contains "load -i $archive") 'archive loaded'
    Assert-True (Test-NoCallLike $r '^tag ') 'no retag needed'
    Assert-True (Test-NoCallLike $r $noPullOrBuild) 'nothing pulled or built'
    Assert-True ((Get-Cmds $r) -contains 'compose up -d --no-build') 'services started'
    Show-OnFailure $r

    $script:Test = 'upgrade -Image <archive>.tar.gz (strategy-app:<version> inside is tagged)'
    Write-Host "  $($script:Test)"
    $archiveNext = New-Archive $next
    $r = Invoke-Script $offline 'upgrade.ps1' @('-Image', $archiveNext) @{ NEW_VERSION = $next; FAKE_DOCKER_IMAGES = "$($ghcr):$current"; FAKE_DOCKER_LOAD_IMAGE = "strategy-app:$next" }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ($r.Output -match 'Checksum verified') 'checksum verified'
    Assert-True ((Get-Cmds $r) -contains "tag strategy-app:$next $($ghcr):$next") "tagged as $($ghcr):$next"
    Assert-True (Test-NoCallLike $r $noPullOrBuild) 'nothing pulled or built'
    Assert-True (Test-Before $r "load -i $archiveNext" (Get-FirstDump $r)) 'load before the pre-upgrade backup'
    Assert-True ((Get-EnvLine $offline 'APP_VERSION') -eq $next) "APP_VERSION=$next saved"
    Show-OnFailure $r

    $script:Test = 'install -Image with a checksum mismatch (refused)'
    Write-Host "  $($script:Test)"
    $mirror = New-Release 'mirror'
    $bad = New-Archive $after -BadChecksum
    $r = Invoke-Script $mirror 'install.ps1' @('-Image', $bad) @{ FAKE_DOCKER_IMAGES = 'none' }
    Assert-True ($r.Code -eq 4) "exit code 4 (was $($r.Code))"
    Assert-True ($r.Output -match 'Checksum mismatch') 'mismatch reported'
    Assert-True (Test-NoCallLike $r '^load ') 'nothing loaded'
    Assert-True (Test-NoCallLike $r '^compose up') 'nothing started'
    Show-OnFailure $r

    $script:Test = 'install -Image for a registry mirror (APP_IMAGE): ghcr.io name tagged'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $mirror 'install.ps1' @('-Image', $archive) @{ APP_IMAGE = 'registry.example.org/strategy/strategy-app'; FAKE_DOCKER_IMAGES = 'none'; FAKE_DOCKER_LOAD_IMAGE = "$($ghcr):$current" }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ((Get-Cmds $r) -contains "tag $($ghcr):$current registry.example.org/strategy/strategy-app:$current") 'tagged with the mirror name'
    Assert-True ((Get-EnvLine $mirror 'APP_IMAGE') -eq 'registry.example.org/strategy/strategy-app') 'APP_IMAGE saved'
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install -NoPull (image on this machine)'
    Write-Host "  $($script:Test)"
    $local = New-Release 'local'
    $r = Invoke-Script $local 'install.ps1' @('-NoPull')
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ((Get-Cmds $r) -contains "image inspect $($ghcr):$current") 'image checked'
    Assert-True (Test-NoCallLike $r $noPullOrBuild) 'nothing pulled or built'
    Assert-True ((Get-Cmds $r) -contains 'compose up -d --no-build') 'services started'
    Show-OnFailure $r

    $script:Test = 'install -NoBuild (deprecated alias of -NoPull)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $local 'install.ps1' @('-NoBuild')
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ($r.Output -match '-NoBuild is deprecated: use -NoPull') 'deprecation warning'
    Assert-True (Test-NoCallLike $r $noPullOrBuild) 'nothing pulled or built'
    Show-OnFailure $r

    $script:Test = 'upgrade of a pre-1.0.2 installation (no APP_IMAGE, image built as strategy-app:<version>)'
    Write-Host "  $($script:Test)"
    $envLocal = Join-Path $local 'deploy/.env'
    $kept = @([IO.File]::ReadAllLines($envLocal, $utf8) | Where-Object { -not $_.StartsWith('APP_IMAGE=') })
    [IO.File]::WriteAllText($envLocal, (($kept -join "`n") + "`n"), $utf8)
    $r = Invoke-Script $local 'upgrade.ps1' @() @{ NEW_VERSION = $next; FAKE_DOCKER_IMAGES = "strategy-app:$current" }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True ((Get-EnvLine $local 'APP_IMAGE') -eq $ghcr) 'APP_IMAGE default written to .env'
    Assert-True (Test-Before $r "tag strategy-app:$current $($ghcr):$current" (Get-FirstDump $r)) 'old image tagged with the release name before the backup'
    $pull = @(Find-Call $r 'compose pull migrate app')
    Assert-True ($pull.Count -eq 1 -and $pull[0].env.APP_VERSION -eq $next) "new image pulled as $next"
    Show-OnFailure $r

    $script:Test = 'install -NoPull without the image'
    Write-Host "  $($script:Test)"
    $noImage = New-Release 'no-image'
    $r = Invoke-Script $noImage 'install.ps1' @('-NoPull') @{ FAKE_DOCKER_IMAGES = 'none' }
    Assert-True ($r.Code -eq 4) "exit code 4 (was $($r.Code))"
    Assert-True ($r.Output -match "-NoPull: image $([regex]::Escape("$($ghcr):$current")) is not on this machine") 'message names the image'
    Assert-True (Test-NoCallLike $r '^compose up') 'nothing started'
    Show-OnFailure $r

    $script:Test = 'install when the pull fails (offline machine)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $noImage 'install.ps1' @() @{ FAKE_DOCKER_IMAGES = 'none'; FAKE_DOCKER_FAIL = 'compose pull=1' }
    Assert-True ($r.Code -eq 4) "exit code 4 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    Assert-True ($r.Output -match 'run the script again with -Image <file>') 'points to -Image'
    Assert-True (Test-NoCallLike $r '^compose up') 'nothing started'
    Show-OnFailure $r

    $script:Test = 'install with -Build and -NoPull (refused)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $noImage 'install.ps1' @('-Build', '-NoPull')
    Assert-True ($r.Code -eq 2) "exit code 2 (was $($r.Code))"
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install -Build with a broken .git (git fails on stderr)'
    Write-Host "  $($script:Test)"
    if (Get-Command git -CommandType Application -ErrorAction SilentlyContinue) {
        $broken = New-Release 'broken-git' -BrokenGit
        $r = Invoke-Script $broken 'install.ps1' @('-Build')
        Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
        Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
        $build = @(Find-Call $r 'compose build migrate')
        Assert-True ($build.Count -eq 1 -and $build[0].env.GIT_REVISION -eq '') 'failed rev-parse = no revision'
        Show-OnFailure $r
    } else {
        Write-Host '    skipped: git is not installed'
    }

    $script:Test = 'install with a failing image build'
    Write-Host "  $($script:Test)"
    $other = New-Release 'build-fails'
    $r = Invoke-Script $other 'install.ps1' @('-Build') @{ FAKE_DOCKER_FAIL = 'compose build=17' }
    Assert-True ($r.Code -ne 0) "non-zero exit code (was $($r.Code))"
    Assert-True ($r.Output -match 'docker compose build migrate failed \(exit 17\)') 'error names the failed command and its exit code'
    Assert-True (-not ((Get-Cmds $r) -contains 'compose up -d --no-build')) 'nothing started'
    Show-OnFailure $r

    $script:Test = 'Docker not running'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $other 'backup.ps1' @() @{ FAKE_DOCKER_FAIL = 'info=1' }
    Assert-True ($r.Code -eq 10) "exit code 10 (was $($r.Code))"
    Assert-True ($r.Output -match 'Docker is installed but not running') 'message unchanged'
    Show-OnFailure $r
} finally {
    if (-not $KeepFiles) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}

Write-Host ''
if ($script:Failures.Count -gt 0) {
    Write-Host "$($script:Failures.Count) of $($script:Count) assertions failed:" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "All $($script:Count) assertions passed (PowerShell $psVersion)."
