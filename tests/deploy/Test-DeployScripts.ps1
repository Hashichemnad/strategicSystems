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
    foreach ($k in 'APP_PORT', 'POSTGRES_PORT', 'GOTENBERG_PORT', 'APP_VERSION', 'NEW_VERSION', 'FAKE_DOCKER_FAIL', 'GIT_REVISION') {
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
    if ($x.Length -ne $y.Length) { return $false }
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
    $build = @(Find-Call $r 'compose build migrate')
    Assert-True ($build.Count -eq 1) 'image built once (compose build migrate)'
    if ($build.Count -eq 1) {
        Assert-True ($build[0].env.GIT_REVISION -eq '') 'no .git: GIT_REVISION empty'
        Assert-True ($build[0].env.APP_VERSION -eq '1.0.0') 'APP_VERSION from deploy/VERSION'
        Assert-True ($build[0].env.BUILD_CREATED -match '^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$') 'BUILD_CREATED in UTC'
    }
    Assert-True ($cmds -contains 'compose up -d --no-build') 'services started'
    Assert-True ([Array]::IndexOf($cmds, 'compose build migrate') -lt [Array]::IndexOf($cmds, 'compose up -d --no-build')) 'build before start'
    $envFile = Join-Path $release 'deploy/.env'
    Assert-True (Test-Utf8File $envFile) '.env is UTF-8 without BOM with LF line endings'
    Assert-True ((Get-EnvLine $release 'POSTGRES_PASSWORD') -match '^[0-9a-f]{48}$') 'generated POSTGRES_PASSWORD'
    Assert-True ((Get-EnvLine $release 'STRATEGY_APP_PASSWORD') -match '^[0-9a-f]{48}$') 'generated STRATEGY_APP_PASSWORD'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq '1.0.0') 'APP_VERSION saved'
    Assert-True ((Get-EnvLine $release 'APP_PORT') -eq '18431') 'APP_PORT from the environment saved'
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
        Assert-True ($backups[0].Name -match '^strategy-1\.0\.0-\d{8}T\d{6}Z$') "folder name strategy-<version>-<UTC> ($($backups[0].Name))"
        Assert-True (Test-SameBytes (Join-Path $b 'database.dump') (Join-Path $r.State 'emitted-pg_dump.bin')) 'database.dump byte-identical to pg_dump output'
        Assert-True (Test-SameBytes (Join-Path $b 'files.tar.gz') (Join-Path $r.State 'emitted-files.tar.gz')) 'files.tar.gz byte-identical to tar output'
        Assert-True (Test-SameBytes (Join-Path $b 'env') $envFile) 'env copied'
        foreach ($f in 'SHA256SUMS', 'manifest.json', 'table-counts.tsv') { Assert-True (Test-Utf8File (Join-Path $b $f)) "$f is UTF-8 without BOM, LF" }
        $counts = [IO.File]::ReadAllText((Join-Path $b 'table-counts.tsv'), $utf8)
        Assert-True ($counts -eq "core.users`t3`nkpi.kpi`t5`n") 'table counts as returned by psql'
        $sums = @([IO.File]::ReadAllLines((Join-Path $b 'SHA256SUMS'), $utf8) | Where-Object { $_ })
        Assert-True ($sums.Count -eq 4) "SHA256SUMS lists 4 parts ($($sums.Count))"
        $bad = 0
        foreach ($line in $sums) {
            if ($line -notmatch '^([0-9a-f]{64})  (\S+)$') { $bad++; continue }
            if ((Get-FileHash -Algorithm SHA256 (Join-Path $b $Matches[2])).Hash.ToLowerInvariant() -ne $Matches[1]) { $bad++ }
        }
        Assert-True ($bad -eq 0) 'SHA256SUMS lines in sha256sum format and correct'
        $manifest = [IO.File]::ReadAllText((Join-Path $b 'manifest.json'), $utf8) | ConvertFrom-Json
        Assert-True ($manifest.version -eq '1.0.0' -and $manifest.format -eq 1 -and $manifest.consistent -eq $true) 'manifest version, format, consistent'
        Assert-True ($manifest.tables -eq 2 -and $manifest.rows -eq 8) 'manifest tables 2, rows 8'
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
    $script:Test = 'upgrade to 1.0.1 (release folder without .git)'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'upgrade.ps1' @() @{ NEW_VERSION = '1.0.1' }
    Assert-True ($r.Code -eq 0) "exit code 0 (was $($r.Code))"
    Assert-True (Test-NoNativeErrors $r) 'no stderr turned into PowerShell errors'
    $cmds = @(Get-Cmds $r)
    $build = @(Find-Call $r 'compose build migrate')
    Assert-True ($build.Count -eq 1 -and $build[0].env.APP_VERSION -eq '1.0.1' -and $build[0].env.GIT_REVISION -eq '') 'new image built as 1.0.1 without revision'
    $dump = [Array]::IndexOf($cmds, ($cmds | Where-Object { $_ -like 'compose exec -T postgres pg_dump *' } | Select-Object -First 1))
    Assert-True ($dump -gt [Array]::IndexOf($cmds, 'compose build migrate')) 'pre-upgrade backup after the build'
    Assert-True ($cmds -contains 'compose run --rm migrate') 'migrations as the owner'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq '1.0.1') 'APP_VERSION=1.0.1 saved'
    Assert-True (Test-Utf8File $envFile) '.env still UTF-8 without BOM, LF'
    Assert-True (@(Get-ChildItem -Directory (Join-Path $release 'backups')).Count -eq 2) 'pre-upgrade backup written'
    Assert-True ($r.Output -match 'Upgrade to 1\.0\.1 complete') 'success message unchanged'
    # (The child's console encoding decides how the non-ASCII part of the path is shown; the file round trip itself is UTF-8.)
    Assert-True ($r.Output -match 'checked the application: .*strategy-1\.0\.0-\d{8}T\d{6}Z') 'pre-upgrade backup folder handed over from backup.ps1'
    Show-OnFailure $r

    $script:Test = 'upgrade with failing migrations'
    Write-Host "  $($script:Test)"
    $r = Invoke-Script $release 'upgrade.ps1' @('-NoBuild') @{ NEW_VERSION = '1.0.2'; FAKE_DOCKER_FAIL = 'compose run --rm migrate=3' }
    Assert-True ($r.Code -eq 7) "exit code 7 (was $($r.Code))"
    Assert-True ($r.Output -match 'restore\.ps1 -Backup') 'rollback command printed'
    Assert-True ((Get-EnvLine $release 'APP_VERSION') -eq '1.0.1') 'APP_VERSION unchanged'
    Show-OnFailure $r

    # -----------------------------------------------------------------------------------------------------------------------------
    $script:Test = 'install with a broken .git (git fails on stderr)'
    Write-Host "  $($script:Test)"
    if (Get-Command git -CommandType Application -ErrorAction SilentlyContinue) {
        $broken = New-Release 'broken-git' -BrokenGit
        $r = Invoke-Script $broken 'install.ps1'
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
    $r = Invoke-Script $other 'install.ps1' @() @{ FAKE_DOCKER_FAIL = 'compose build=17' }
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
