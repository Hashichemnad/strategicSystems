# Phase 18 — Installer, setup wizard, operations, hardening, documentation, release (design note)

SPEC §0.0 (product model: single-tenant, self-hosted, one-time sale, offline networks), §0.5 *Installation & operations*, every
non-functional requirement (§1.10 and each module's), CLAUDE.md §4.4, §14, §16. Read with the phase summaries 0–17 ("Known gaps").

## 1. Scope (restated)

| # | Area | Result |
|---|---|---|
| 1 | Packaging | Production image (non-root, healthcheck, OCI labels, versioned tag `strategy-app:<version>`), compose for installs, documented **non-Docker install** (systemd service + PostgreSQL 16 + Gotenberg container or binary + Tesseract), `deploy/install.sh` (Linux) and `deploy/install.ps1` (Windows Server / Docker Desktop): prerequisites, port check with a clear message (`APP_PORT` / `POSTGRES_PORT` / `GOTENBERG_PORT`), generated secrets in `.env`, build / start, wait for ready, print the setup URL and the one-time setup token |
| 2 | First-run setup wizard | `/setup` in the SPA, only while no administrator exists, opened with a **one-time setup token** printed in the app log (ADR 0150) |
| 3 | Operations | `deploy/backup.sh|ps1`, `restore.sh|ps1`, `upgrade.sh|ps1`; admin **Health page** (ADR 0151); version + release notes in the app (ADR 0152) |
| 4 | Seed packs | Neutral default pack = the component / module seeders that already run on every start (listed and verified); optional **demo pack** loadable from the wizard or `--seed-demo` and removable with `--remove-demo` (ADR 0153) |
| 5 | Leftovers | UAE Pass for Wasel vendors (ADR 0154); MinIO and ClamAV tests against the real containers; intermittent CAP test; "every Quartz job fires on its schedule" test; stock-metric past-period comparison on the live path; review of every earlier known gap (release notes §Known limitations) |
| 6 | Security | `docs/security/asvs-l2.md` (ASVS 4.0.3 L2, per requirement), `docs/security/threat-model.md` (web, Wasel, API tokens, MCP, AI); fixes: SSRF guard for outbound calls (ADR 0155), headers / cookies / CSP / limits re-verified by tests; image + dependency scans (Trivy, `npm audit`, `dotnet list package --vulnerable`) |
| 7 | Performance | k6 scripts in `tests/perf/` run as a container against a seeded installation (ADR 0156); report `docs/performance.md` |
| 8 | Accessibility | `@axe-core/playwright` (MPL-2.0) spec over every main screen in `ar` and `en`; fixes; report `docs/accessibility.md` |
| 9 | Regression | full E2E (ar + en) and all quality gates |
| 10 | Documentation | `docs/guides/{en,ar}/`: installation, administrator, user guides per system, MCP connection guide; release notes 1.0 |
| 11 | Release | version `1.0.0` (`Directory.Build.props` `Version`, web `package.json`), `CHANGELOG.md`, `docs/THIRD_PARTY_LICENSES.md`, CycloneDX SBOM of the image (`deploy/sbom.sh`, ADR 0157), git tag `v1.0.0` |

## 2. Setup wizard (ADR 0150)

**Two stages**, so that the unauthenticated surface is as small as possible:

1. **Bootstrap (token)** — public endpoints under `/api/v1/setup`, available only while *no user holds the platform administrator role*:
   - `GET /setup/status` → `{ needsSetup, setupCompleted, version }` (public; reveals nothing else).
   - `POST /setup/verify-token` → 204 / 403 (rate limited with the `auth` policy).
   - `POST /setup/bootstrap` (header `X-Setup-Token`) with organisation profile (name AR / EN, short code, time zone, currency), branding
     (primary colour, email header / footer), languages (default language, Hijri, Arabic-Indic digits) and the first administrator
     (email, password, employee name AR / EN). One transaction-like sequence: saves Regional + Branding settings, creates the root unit
     and the administrator's employee, the user (password policy), grants the platform administrator roles, **consumes the token**.
   - The token: 32 random bytes (base64url), generated at start-up when no administrator exists and no unconsumed token is stored; only its
     SHA-256 is stored (`core.setup_state`, single row), the clear value is **written to the log once** (warning level, clearly framed) and
     can be re-printed with `--setup-token` (rotates it). Constant-time compare. Expires after 24 h (a restart issues a new one). Every
     attempt is audited (`setup.token.verify` success / failure, channel `system`).
2. **Guided configuration (signed in)** — the administrator signs in, is forced through MFA enrolment (existing policy), and the SPA
   continues the wizard at `/setup` because `setupCompleted` is false. Steps reuse the **existing, permission-checked settings
   endpoints**: email server (+ *Send test email*), authentication provider (optional), AI provider (optional, skippable, *Test connection*),
   business hours and holidays, organisation-structure import (templates download → upload units, then employees), systems to enable,
   optional demo pack. `POST /setup/complete` (permission `settings.manage`) sets `setupCompleted`. Each step can be skipped and done later
   in Settings; the wizard can be reopened from Settings → *Setup wizard* until completed.

`--create-admin` stays the scripted alternative; when used, the bootstrap stage is closed (an administrator exists) and the admin may still
run stage 2. Integration tests: token required / wrong / reused / after admin exists → 403/404; bootstrap creates the admin; audit entries;
E2E: whole wizard in ar and en on an empty database (separate Playwright project `setup` against a fresh database).

## 3. Health page (ADR 0151)

`GET /api/v1/admin/health` (permission `platform.health.view`, seeded to the platform administrator role) aggregates checks, each
`{ code, status: ok|warning|error|notConfigured, messageKey, params, details }`:
database (latency, server version, size), migrations (pending per module), scheduled jobs (per job: last run, status, next fire time,
failures in 24 h; scheduler running), outbox (pending, failed, oldest pending age), email (SMTP configured, last delivery, failures 24 h,
optional *test connection*), AI provider (enabled, last call, failures; *Test connection* reuses the AI settings action), OCR (Tesseract
found, languages), storage (provider, writable probe, free disk space of the files path and of the data protection / temp path; S3 bucket
reachable), Gotenberg reachable, certificate expiry (Data Protection certificate when configured; `Health:CertificatePaths` for the TLS
certificate of the reverse proxy when mounted), audit hash-chain last verification result, version / build / uptime.
SPA page *Settings → Health* (also linked from the user menu for administrators). `/health/live` and `/health/ready` stay as they are.

## 4. Version and release notes (ADR 0152)

`Version` 1.0.0 in `Directory.Build.props` (`InformationalVersion` + git commit through `SourceRevisionId` when available); image label
`org.opencontainers.image.version`. `GET /api/v1/system/version` (authenticated) → version, commit, build date. Release notes as bilingual
Markdown resources in the Host (`ReleaseNotes/{en,ar}/1.0.0.md`) served by `GET /api/v1/system/release-notes` and shown in *About* (user
menu). `CHANGELOG.md` at the root.

## 5. Backup, restore, upgrade

* **Backup** (`deploy/backup.sh` / `.ps1`): `pg_dump -Fc` inside the postgres container (includes `core.data_protection_keys`), tar of the
  files volume, copy of `deploy/.env`, optional Data Protection certificate; a `manifest.json` (version, timestamp, sha256 of each part);
  output `backups/strategy-<version>-<UTC timestamp>/`.
* **Restore** (`restore.sh` / `.ps1 <folder>`): verifies checksums, stops `app`, recreates the database (`pg_restore --clean --if-exists`
  as owner), restores files, optionally `.env`, runs `migrate` (grants), starts `app`, waits for `/health/ready`, then **verifies**: row
  counts of key tables equal those recorded in the manifest and the audit hash chain verifies (`--verify-audit-chain` host command).
* **Upgrade** (`upgrade.sh` / `.ps1`): pre-upgrade backup (mandatory, skip only with `--no-backup`), build / load the new image, `migrate`
  (as owner, grants runtime role), start `app`, wait for ready; on failure prints the rollback: restore the backup with the previous image
  tag (`APP_VERSION`). Documented in the installation guide; tested end-to-end in this environment (backup → wipe → restore; upgrade from
  the Phase 17 build).

## 6. Seed packs (ADR 0153)

* **Neutral default pack**: unchanged mechanism — every component / module seeder runs on start (lookups, permissions catalogue, default
  roles and role-permission maps, notification templates, approval process definitions, Appendix A defaults). No organisation data. An
  integration test lists what an empty installation contains after `--migrate` and the first start (counts per catalogue, no employees,
  no units).
* **Demo pack**: `IDemoPackContributor` per module (Contracts-free: registered in the Host); creates a demo organisation, demo users
  (password from the command / wizard, MFA off, flagged), sample records across all systems through each module's own handlers (audited,
  channel `import`), and records every created id in `core.demo_record`. Load: wizard step or `--seed-demo --password-file`. Remove:
  `--remove-demo` (and Settings → Setup → *Remove demo data*): business records are **deactivated / cancelled** per CLAUDE.md §6 (no
  hard delete except never-submitted drafts), demo users locked and disabled, demo units deactivated. Recorded in the audit trail.

## 7. Wasel UAE Pass (ADR 0154)

Vendor UAE Pass reuses the internal OIDC handler pattern with a **separate scheme** (`wasel-uaepass`) bound to the Wasel cookie scheme,
enabled only when the UAE Pass provider is configured *and* `uaePassForVendors` is on. Callback: verified e-mail claim → find the vendor
user by normalised e-mail (principal type `vendor`, active, not expired) → never auto-create → **the second factor is still required**
(the session is half-signed-in until TOTP / e-mail OTP succeeds), audited (`wasel.signin.uaepass`). Tests: unknown e-mail refused, internal
user refused, inactive vendor refused, MFA still required; E2E with a mock OIDC provider (test-only in-process issuer).

## 8. Security (ADR 0155)

* `docs/security/asvs-l2.md`: every ASVS 4.0.3 L2 requirement (V1–V14) with *how met* / *evidence (test, file)* / *N/A reason*.
* `docs/security/threat-model.md`: STRIDE table per surface: web UI + API, Wasel, personal access tokens, MCP (OAuth), AI providers,
  identity providers, outbound webhooks / SMTP, files.
* **SSRF guard**: one `OutboundUrlPolicy` used by AI providers, identity-provider metadata / discovery, Gotenberg / S3 / ClamAV are
  configured by the operator (trusted). Admin-entered URLs: scheme http(s) only, resolve and refuse loopback, link-local
  (169.254.0.0/16, incl. cloud metadata), and — unless `Outbound:AllowPrivateNetworks` (default **true** for on-prem, where providers are
  internal; documented) — private ranges; redirects not followed to refused addresses.
* Re-verified with tests: security headers, cookie flags (`Secure`, `HttpOnly`, `SameSite`), session idle / absolute timeout, lockout,
  rate limits, upload checks, audit chain verification.
* Scans: `trivy image` (Apache-2.0, container, not bundled) — no High / Critical with a fix available; `npm audit`; NuGet vulnerable check.

## 9. Performance (ADR 0156)

k6 (AGPL-3.0, run as an external container, never distributed — dev / test tool only) scripts in `tests/perf/`: record page API set,
Gantt data for a 30-milestone project, executive dashboard, My Work, lists; 500 active projects seeded by the existing volume seeder
(`ExecutivePerformanceTests` data), seeded by the test-project runner `Performance/PerformanceVolumeSeed` instead of a host command (ADR 0158: the seeders write with COPY, test databases only). Notification latency: outbox dispatch measured from
event to Mailpit receipt. Report with p50 / p95 / p99 per NFR in `docs/performance.md`.

## 10. Accessibility

`@axe-core/playwright` (MPL-2.0) spec `e2e/a11y.spec.ts`: every main screen (shell, My Work, each system's list / record / dashboard,
Settings areas, setup wizard, Wasel) in both projects; tags `wcag2a, wcag2aa, wcag21a, wcag21aa`; zero violations required; report
`docs/accessibility.md` (screens covered, rules, findings fixed, manual checks: keyboard, focus, zoom 200 %, 360 px).

## 11. Documentation set

```
docs/guides/en/  installation.md · administration.md · user-<system>.md (portfolio, wasel, kpi, risk, teams, tasks, requests, bench, cap,
                 auditmgmt, dashboards, my-work) · mcp.md
docs/guides/ar/  same files in Arabic
docs/release-notes/1.0.0.md (en + ar) · CHANGELOG.md · docs/security/* · docs/performance.md · docs/accessibility.md
```
Screenshots referenced from `docs/screenshots/phase-*/` (E2E runs).

## 12. ADRs

0150 setup wizard and setup token · 0151 health page · 0152 version and release notes · 0153 seed packs and removable demo pack ·
0154 UAE Pass for Wasel vendors · 0155 outbound URL policy (SSRF) · 0156 performance and scanning tools (k6, Trivy, CycloneDX) ·
0157 backup / restore / upgrade format.

## 13. Permissions, events, jobs

New permissions: `platform.health.view`, `platform.setup.manage` (wizard stage 2, demo pack), both on the platform administrator role.
No new integration events. No new scheduled jobs (health reads job state). Audit: setup token attempts, bootstrap, demo load / removal,
health *test* actions.

## 14. Phase 19 — Windows PowerShell 5.1, ZIP releases, slow networks (stream O, ADR 0163)

SPEC-ADDENDUM A4–A6. Closes the known gap "Windows scripts only run under PowerShell 7 on Linux". No change to the backup format, the
compose file's services, the image content or the scripts' messages and exit codes (ADR 0157 stays valid).

**A4 — PowerShell 5.1 and 7.** Audit of `deploy/*.ps1` found these 5.1 failures, fixed as follows:

| Pitfall (Windows PowerShell 5.1) | Where | Fix |
|---|---|---|
| Native stderr redirected (`2>$null`, `*>`, `2>&1`, or the host's own stderr redirected — CI, scheduled task, remote shell) becomes ErrorRecords; with `$ErrorActionPreference='Stop'` the first `docker compose` progress line or `docker info` warning **terminates** the script | `Invoke-Compose`, `Invoke-ComposeQuiet`, `Assert-Docker`, `Wait-App`, `git rev-parse`, `docker load`, `image inspect`, `--verify-audit-chain`, `logs` | one helper `Invoke-Native` in `lib.ps1`: `ErrorActionPreference='Continue'` (and `PSNativeCommandUseErrorActionPreference=$false`) locally, stdout / stderr merged and turned back into plain text lines (shown with `Write-Host`, captured, or discarded), result decided **only by `$LASTEXITCODE`**; on an interactive console with nothing redirected the command keeps the terminal (compose's live progress view). Every native call of the scripts goes through it (except the byte-exact `Invoke-ComposeBinary`, which uses `System.Diagnostics.Process`) |
| A BOM-less UTF-8 script is read as the ANSI code page | `install.ps1` (em dash, middle dot) | every `.ps1` is **ASCII-only**; `tools/check-powershell.ps1` fails on a non-ASCII byte in a script without a BOM |
| `Get-Content` reads BOM-less UTF-8 files as ANSI: `Set-EnvValue` rewrote `.env` and turned the `→` of `.env.example` (and any non-ASCII password or path) into mojibake; the backup path handed from `backup.ps1` to `upgrade.ps1` broke under a non-ASCII user folder | `Get-EnvValue`, `Set-EnvValue`, table counts, `SHA256SUMS`, `manifest.json`, result file | `Read-Utf8Lines` / `Read-Utf8Text` (`[IO.File]` with UTF-8); writes were already UTF-8 without BOM + LF (`Write-Utf8Lines`) — now used for every file the `.sh` twins or the container read |
| `$null \| ConvertFrom-Json` is a terminating binding error | image labels in `restore.ps1` | only parse non-empty output, inside `try` |
| PS7-only syntax / parameters (`??`, `?:`, `&&`, `\|\|`, `-Parallel`, `-AsHashtable`, `Join-Path` with 3+ parts, `-AsByteStream`, `$IsWindows`) | none found | guarded by PSScriptAnalyzer `PSUseCompatibleSyntax` (5.1 + 7) and `PSUseCompatibleCommands` (Windows PowerShell 5.1 profile) in CI |
| `Invoke-WebRequest` without `-UseBasicParsing`, TLS 1.2 | not used (the scripts download nothing; the build downloads inside Docker) | rule recorded in ADR 0163 for future scripts |
| `Get-FileHash` / `tar` / `Compress-Archive` | `Get-FileHash` exists since 4.0; archives are made by `tar` **inside** the container (byte-exact through `Process` streams) | unchanged |
| Path separators to Docker | compose accepts Windows paths for `-f` / `--env-file`; `BUILD_CA_FILE` is stored as `./build-ca.local.pem` (relative to the project) | unchanged |

**A5 — ZIP releases (no `.git`).** One function per twin (`Get-SourceRevision` / `source_revision`): the revision is read with git only when
`<release>/.git` exists and git is installed; a failing `git rev-parse` (no repository, a broken `.git`, "dubious ownership") means "no
revision" — stderr discarded, exit code tolerated, no `set -e` abort. Without `.git` git is not run at all, so a ZIP unpacked inside some
other repository does not get that repository's commit as its image label. The version always comes from `deploy/VERSION`.

**A6 — slow networks.** Build arguments (compose `args`, settable in `deploy/.env`, defaults in the Dockerfile and `.env.example`):
`NPM_FETCH_RETRIES=5`, `NPM_FETCH_RETRY_MINTIMEOUT=20000`, `NPM_FETCH_RETRY_MAXTIMEOUT=120000`, `NPM_FETCH_TIMEOUT=600000` (ms) →
`npm_config_*` for `npm ci`; NuGet: `NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10` and `NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=3000`
(NuGet's own per-request retry with back-off, `NUGET_RETRY_HTTP_429=true`), plus `NUGET_RESTORE_ATTEMPTS=3`: a bounded loop around
`dotnet restore` (pause 20 s × attempt; packages already downloaded stay in the HTTP cache, so a later attempt continues) and
`NUGET_RESTORE_DISABLE_PARALLEL=false` (`true` → `--disable-parallel`, fewer simultaneous connections on a very thin line). NuGet has no
setting for its per-request HTTP timeout (100 s, checked against the SDK's NuGet 10 assemblies: only the retry variables exist), so a
longer timeout is achieved by more and slower retries; recorded in ADR 0163. Runtime stage: `APT_RETRIES=5` and `APT_HTTP_TIMEOUT=120`
passed as `-o Acquire::Retries` / `-o Acquire::http::Timeout` / `-o Acquire::https::Timeout` to every `apt-get` (nothing stays in the
image). The defaults change nothing on a fast network. Documented in the installation guide (en + ar) and `deploy/README.md`.

**Tests.**
- `tests/deploy/fake-docker/` — a fake `docker` (Python, standard library only) that writes compose-style progress to **stderr**, records every
  call (arguments without the `-f / --env-file / --profile` prefix, plus `APP_VERSION` / `GIT_REVISION`) as JSON lines, returns scripted exit
  codes (`FAKE_DOCKER_FAIL="<substring>=<code>;…"`) and plays the binary parts (pg_dump bytes with NUL / CR / LF / 0xFF, tar, stdin of
  psql / pg_restore saved for comparison). On Linux a `docker` shell wrapper runs it; on Windows a tiny forwarder `docker.exe` (C# 5, compiled
  with the .NET Framework `csc.exe` of the runner) — `System.Diagnostics.Process` (used by `Invoke-ComposeBinary`) only finds `.exe` files.
- `tests/deploy/Test-DeployScripts.ps1` (plain assertions, no Pester; runs under 5.1 and 7, `-Shell` chooses the PowerShell that runs the
  scripts) and `tests/deploy/test-deploy-scripts.sh` (bash): in a temporary copy of `deploy/` **without `.git`** (and once with a broken
  `.git` file, so git fails with stderr): install (fresh, again, build failure), backup (binary integrity, checksums, manifest), restore (dump
  and SQL received, checksum mismatch → 7), upgrade (new version, migration failure → 7), Docker not running → 10; `.env` and the other
  written files: UTF-8 without BOM, LF, non-ASCII line of `.env.example` byte-identical. Output of the scripts is redirected to a file, which
  is exactly the condition that broke 5.1.
- CI: job **`deploy-windows`** on `windows-latest` runs the PowerShell tests under `powershell.exe` (5.1) and `pwsh` (7); job **`deploy-scripts`**
  (ubuntu, independent of the slow image build) runs shellcheck, the parser / encoding check, the bash tests, the PowerShell tests under
  `pwsh` on Linux and PSScriptAnalyzer 1.23.0 (`tests/deploy/Invoke-ScriptAnalysis.ps1`: error-level rules, `PSUseCompatibleSyntax` 5.1 /
  7.0, `PSUseCompatibleCommands` against the analyzer's Windows PowerShell 5.1 profile).
- **Manual acceptance on a real Windows machine with Docker Desktop** (the owner runs it during acceptance): checklist
  `docs/operations/windows-acceptance.md` — install from the ZIP under 5.1, backup, restore, upgrade, then the same under 7.
