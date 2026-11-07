# 0163 — Deploy scripts under Windows PowerShell 5.1, ZIP releases, slow-network builds

- **Status:** Accepted (Phase 19, stream O)
- **Context:** SPEC-ADDENDUM A4–A6. ADR 0157 shipped `.ps1` twins of the install / backup / restore / upgrade scripts, verified only with
  PowerShell 7 on Linux. On Windows PowerShell 5.1 (the PowerShell every Windows Server has) native stderr redirected by PowerShell, or with
  the host's stderr redirected, becomes ErrorRecords, so with `$ErrorActionPreference = 'Stop'` the first `docker compose` progress line,
  a `docker info` warning or a failed `git rev-parse` in a folder without `.git` ends the script. 5.1 also reads BOM-less UTF-8 as ANSI
  (scripts and `Get-Content`). Releases are handed over as ZIP files without `.git`. Customer networks are slow or filtered: `npm ci` /
  `dotnet restore` / `apt-get` fail on the first timeout with the tools' default retries.

## Decision

1. **One native-command helper.** `Invoke-Native` in `deploy/lib.ps1` is the only way the scripts run a native program (except
   `Invoke-ComposeBinary`, which streams bytes through `System.Diagnostics.Process`). It sets `ErrorActionPreference = 'Continue'` and
   `PSNativeCommandUseErrorActionPreference = $false` for its own scope, merges stderr into the output (`2>&1`) and converts the
   ErrorRecords back to plain lines (shown, captured or discarded), and returns `{ Code; Output }` decided **only by the exit code**. On an
   interactive console with neither output stream redirected it runs the command without redirection so compose keeps its live progress
   view. `Invoke-Compose` throws on a non-zero code exactly as before; messages and exit codes of the scripts are unchanged.
2. **Encodings.** Every `.ps1` is ASCII-only (`tools/check-powershell.ps1` fails on a non-ASCII byte without a BOM). Files the `.sh` twins,
   compose or the container read (`.env`, `SHA256SUMS`, `manifest.json`, `table-counts.tsv`, SQL on stdin, the backup result file) are read
   and written through `[IO.File]` as UTF-8 **without BOM** with LF line endings (`Read-Utf8Lines`, `Read-Utf8Text`, `Write-Utf8Lines`).
   Never `Set-Content` / `Out-File` / `>` for these files.
3. **5.1 compatibility rules** for these scripts (checked by PSScriptAnalyzer `PSUseCompatibleSyntax` for 5.1 / 7 and
   `PSUseCompatibleCommands` against the Windows PowerShell 5.1 profile): no `??`, `?:`, `&&` / `||`, `-Parallel`,
   `ConvertFrom-Json -AsHashtable` / `-Depth`, `Join-Path` with more than two parts, `Get-Content -AsByteStream`, `$IsWindows` (use
   `[Environment]::OSVersion.Platform`), no classes / `using namespace`; no empty-string or embedded-double-quote native arguments (5.1
   drops / strips them — SQL goes on stdin); `ConvertFrom-Json` only on non-empty input. A future script that downloads must use
   `Invoke-WebRequest -UseBasicParsing` and enable TLS 1.2 (`[Net.ServicePointManager]::SecurityProtocol`) on 5.1.
4. **ZIP releases.** `Get-SourceRevision` / `source_revision` run git only when `<release>/.git` exists and git is installed; any failure
   (no repository, broken `.git`, dubious ownership) means "no revision": stderr discarded, exit code tolerated, no `set -e` abort. A ZIP
   unpacked inside another repository therefore never takes that repository's commit. The version is always `deploy/VERSION`.
5. **Slow networks.** Build arguments with defaults (all overridable in `deploy/.env`): npm `fetch-retries` 5, `fetch-retry-mintimeout`
   20 s, `fetch-retry-maxtimeout` 120 s, `fetch-timeout` 600 s; NuGet `NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT` 10,
   `NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS` 3000, `NUGET_RETRY_HTTP_429=true`, a bounded loop of `NUGET_RESTORE_ATTEMPTS` (3)
   `dotnet restore` runs with a growing pause, optional `--disable-parallel`; apt `Acquire::Retries` 5 and HTTP(S) timeouts 120 s through
   `-o` options. NuGet 10 has no setting for the per-request HTTP timeout (the SDK assemblies only read the retry variables), so a longer
   effective timeout comes from more, slower retries plus whole-restore retries that reuse the HTTP cache.
6. **Tests without Windows containers.** GitHub's Windows runners run only Windows containers, so the scripts are tested against a fake
   `docker` (Python standard library; a C# 5 forwarder `docker.exe` compiled with the runner's .NET Framework `csc.exe` on Windows, because
   `Process.Start("docker")` only resolves `.exe`). Plain assertion scripts (`tests/deploy/`), no Pester, so the same harness runs under 5.1
   and 7. Real Docker Desktop is covered by a manual acceptance checklist the owner runs (`docs/operations/windows-acceptance.md`).

## Consequences

- The Windows scripts no longer depend on how the console or a CI agent redirects output. Non-interactive runs show compose's plain
  progress lines instead of the live view.
- New CI jobs `deploy-windows` (5.1 + 7 on `windows-latest`) and `deploy-scripts` (Linux: bash and pwsh tests, PSScriptAnalyzer).
- The npm / NuGet / apt defaults only take effect when something fails; a failed network still fails the build, just later and with
  the tools' own messages.
