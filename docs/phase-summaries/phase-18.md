# Phase 18 — Installer, setup wizard, operations, hardening, documentation, release: summary

Status: **built — awaiting owner acceptance** · version **1.0.0**. Design note: [`docs/design/release.md`](../design/release.md).
ADRs 0150–0158 (**proposed**, for the owner's acceptance). Release notes: *About* in the application
([en](../../src/Host/Strategy.Host/ReleaseNotes/en/1.0.0.md) · [ar](../../src/Host/Strategy.Host/ReleaseNotes/ar/1.0.0.md)),
[`CHANGELOG.md`](../../CHANGELOG.md).

## What was built

| Area | Result |
|---|---|
| **Packaging** (ADR 0157) | Image `strategy-app:<version>` (non-root, read-only root file system with `/tmp` tmpfs, `no-new-privileges`, no capabilities, healthcheck, OCI labels, 607 MB — was 1.35 GB), `apt-get upgrade` in the runtime stage; compose: PostgreSQL and Gotenberg on **127.0.0.1 only**, `APP_BIND`, log rotation, `SECURE_COOKIES`, `OUTBOUND_ALLOW_PRIVATE_NETWORKS`; the image is built or loaded locally, never pulled |
| **Install** | `deploy/install.sh` (Linux) and `deploy/install.ps1` (Windows Server / Docker Desktop, PowerShell 5.1 + 7): generated passwords in `deploy/.env`, port check that stops before anything starts and names `APP_PORT` / `POSTGRES_PORT` / `GOTENBERG_PORT`, build or `--image <archive>` (offline), start, wait for ready, print the address and the setup token. Non-Docker install: `deploy/linux/README.md`, `deploy/systemd/strategy.service`, `deploy/nginx/strategy.conf` (HTTPS, forwarded headers, WebSockets) |
| **Setup wizard** (ADR 0150) | One-time setup token (32 random bytes, SHA-256 stored in `identity.setup_state`, constant-time compare, 24 h, logged once at warning level, `--setup-token`), public `GET /setup/status`, `POST /setup/verify-token`, `POST /setup/bootstrap` (auth rate limit, audited) → organisation profile, branding, languages, first administrator (platform administrator roles); stage 2 after sign-in + MFA enrolment: e-mail (+ test), sign-in providers, AI, business hours / holidays, import, systems, demo data; `POST /setup/complete`; permission `settings.setup.manage`. `--create-admin` stays |
| **Operations** (ADR 0157) | `backup` (database incl. Data Protection keys, files, `.env`, optional certificate, row count of every table, checksums, manifest), `restore` (checksums first, row counts compared, migrate + grants, **`--verify-audit-chain`**, health), `upgrade` (new image while the old runs, mandatory pre-upgrade backup, migrations as owner, rollback command) — bash and PowerShell, interchangeable backups |
| **System health** (ADR 0151) | `GET /api/v1/settings/health` (`settings.health.view`): version / uptime, Data Protection, certificates (`Health:CertificatePaths`), database, migrations per schema, scheduler + every job, outbox, e-mail, PDF, AI, OCR, storage, disk (floors capped for small volumes), ClamAV, audit chain; message keys only; a failing / slow (10 s) check never hides the others. Settings → System health (ar + en) |
| **Version and release notes** (ADR 0152) | `Version` 1.0.0 + commit; `/api/v1/system/version`, `/api/v1/system/release-notes`; *About* dialog; default language in Regional settings |
| **Seed packs** (ADR 0153) | Neutral defaults verified by a test (catalogues present, no units / people / demo records); **demo pack**: `IDemoPackContributor` per system (platform + all 11 systems) creating records through each module's own commands as demo people (audited, channel `import`), tracked in `core.demo_record`; `--seed-demo`, `--remove-demo`, `POST/DELETE /api/v1/setup/demo-pack`, Settings → Demo data; removal cancels / deactivates, never hard-deletes; idempotent |
| **Wasel UAE Pass** (ADR 0154) | Vendor flow on the installation's UAE Pass provider (one redirect address); matched by e-mail to **vendor** accounts only, never created; internal / unknown / suspended / inactive refused alike; **second factor still required**; audited; Development-only mock issuer (proved absent in Production) |
| **Security** (ADR 0155) | Outbound URL policy (SSRF) on save and at connect time (AI SDKs, OIDC / UAE Pass back channel, IdP test connection), redirects not followed; `Security:SecureCookies`; offline **common-password check** (SecLists 10 k, MIT; base word and e-mail name); **400 instead of 500** for incomplete bodies on every POST / PUT (48 endpoints, regression test); ASVS 4.0.3 L2 checklist, threat model, scan results in `docs/security/` |
| **Performance** (ADRs 0156, 0158) | k6 scripts `tests/perf/` + `run.sh` against a Release host on the Phase 17 volume (test-only bulk seed — not a product command): every NFR met (below) |
| **Accessibility** | `@axe-core/playwright` (MPL-2.0) `web/e2e/a11y.spec.ts`: ~550 screen states per run in ar + en (all main screens, 42 Settings areas, 17 record pages with every tab, dark subset), keyboard / focus / reflow checks; ~1 000 element findings fixed (contrast tokens, accessible names, grid roles, scroll regions, dialog focus return, 360 px reflow, setup wizard stepper) — `docs/accessibility.md` |
| **Documentation** | `docs/guides/{en,ar}/`: installation, administration, user guides (Portfolio, Wasel, KPI, Risk, Teams, Tasks, Requests / Document Registry / IP, Benchmarking, CAP, Audit, Dashboards, My Work), MCP connection guide (Claude Desktop, claude.ai, Claude Code); `docs/operations/` (verification, seed packs); `docs/release-notes/` |
| **Release** | `1.0.0` in `Directory.Build.props` / `deploy/VERSION`, CHANGELOG, THIRD_PARTY_LICENSES (axe-core, SecLists list, OCR packages), CycloneDX SBOM `release/sbom/strategy-app-1.0.0.cdx.json` (Syft; 4 645 components), CI `docker` job: versioned image, Trivy gate, SBOM artifact, shellcheck, PowerShell syntax |

## Earlier-phase leftovers

| Item | Outcome |
|---|---|
| UAE Pass for Wasel vendors (Phase 11) | **Closed** (above) |
| S3-compatible storage and ClamAV against real containers (Phases 3 / 5) | **Closed** — `Files/RealStorageAndScanTests` (`Category=Containers`): SeaweedFS by default (MinIO images are no longer published on Docker Hub; `STRATEGY_TEST_S3_IMAGE` selects MinIO), real ClamAV with EICAR. **Defect found and fixed:** the AWS SDK's chunked upload stored chunk signatures in the content on non-MinIO servers |
| Intermittent `CapApiTests.Due_dates_follow_the_priority_in_working_time` (Phase 17) | **Closed** — root cause: the test froze the clock *before* the day its role grants started; it passed only while an earlier access snapshot stayed cached (process-wide change signal). Test world now created on the test date; 29 / 30 repeated runs passed (the one failure: disk full) |
| Every Quartz job fires on its schedule (Phase 3–17 defect) | **Closed** — `Scheduling/JobSchedulesFireTests`: all 27 jobs, stored trigger = cron in the installation time zone, fake time moved to each next fire time, schedule-fired (not manual) successful run; reintroducing the old listener bug makes it fail |
| Past-period comparison of stock metrics on the live path (Phase 17) | **Open** — needs per-viewer monthly snapshots (unit facts cannot be filtered to one user's records); listed in the release notes |
| All other known gaps of phases 0–17 | Reviewed one by one: [`docs/release-notes/known-gaps-review.md`](../release-notes/known-gaps-review.md) (closed earlier with evidence / closed now / by design / open with reason and impact) |

## Acceptance checks

| Check | Evidence |
|---|---|
| Fresh Linux server: one command installs; setup wizard completes; first admin logs in | [`docs/operations/verification-1.0.md`](../operations/verification-1.0.md) #2–5 and #14–18: full multi-stage build from a fresh clone, `install.sh` on empty volumes, token 403 / 204 / bootstrap 200 / reuse 403, admin login → MFA enrolment → signed in, health page; E2E `setup.spec.ts` (ar + en) walks the whole wizard on empty databases (30 screenshots) |
| Upgrade from the previous build with data preserved; restore from backup verified | verification #6–9: upgrade 0.17.0 → 1.0.0 (bash and PowerShell) with employees, photos and audit chain intact; rollback; backup → delete every volume → new install with new passwords → restore: 229 tables identical, audit chain intact, administrator signs in |
| ASVS checklist completed with no open high issues; performance NFRs met; WCAG 2.1 AA audit passed | [`docs/security/asvs-l2.md`](../security/asvs-l2.md): 279 requirements — 235 met, 22 partial, 15 N/A, 6 operator, **1 residual** (composition rules on by default, configurable), no open high; image scan 0 High / Critical; [`docs/performance.md`](../performance.md): every NFR passes; [`docs/accessibility.md`](../accessibility.md): 0 axe WCAG 2.1 A / AA violations in ar + en |
| Admin, installation and user guides delivered in Arabic and English | [`docs/guides/`](../guides/) |

### Performance (Release host, 4 vCPU shared with other work, Phase 17 volume, 100 concurrent users)

| NFR | p95 | Target |
|---|---|---|
| Project record page | 148 ms | < 3 s |
| Gantt, 30 milestones | 137 ms | < 5 s |
| Executive dashboard | 162 ms | < 3 s |
| My Work | 651 ms (baseline) | < 3 s |
| Lists | 97 ms | < 3 s |
| 735 active projects browsed | 0 errors / 55 695 requests | — |
| Notification e-mail | max 1.07 s | < 5 min |

At 300 users the 4 CPUs saturate (≈ 174 req/s, 0 errors, p95 3.5–3.9 s): sizing guide in `docs/performance.md`.

## Quality gates run (final, on the merged branch)

| Gate | Result |
|---|---|
| `dotnet build Strategy.slnx -c Release` (SDK **10.0.403**, as in the image / CI) | 0 warnings, 0 errors |
| `dotnet format Strategy.slnx --verify-no-changes` | clean |
| `dotnet test --solution Strategy.slnx` (unit, architecture, integration incl. real S3 / ClamAV / Mailpit / Gotenberg, OCR with Tesseract) | **4 930 / 4 930** |
| `tools/check-vulnerable.sh`, `tools/check-api-client.sh` | no High / Critical; OpenAPI and TS client up to date |
| Web: lint, format:check, typecheck, Vitest, build | pass; Vitest 378 / 378 |
| `npm audit --audit-level=high`, `lint:licenses` | no High / Critical (8 moderate, dev tooling); 618 packages allowed |
| Image: Trivy (High / Critical with a fix), SBOM | 0; CycloneDX generated |
| Playwright E2E (`ar` + `en`, incl. `a11y.spec.ts`) | E2E_RESULT |
| Setup wizard E2E (`playwright.setup.config.ts`, fresh databases) | 2 / 2 (ar + en) |

## Defects found while verifying (fixed in this phase)
- Image build failed with the current .NET 10 SDK (stricter IDE0005) although the local build passed — local SDK aligned with the image.
- S3 chunked uploads corrupted content on S3-compatible servers other than MinIO / AWS.
- Audit Management AI extraction failed (500) when two unit codes differ only in case.
- 48 POST / PUT endpoints answered 500 instead of 400 for incomplete bodies; Risk PDF exports 500 when the PDF service is not configured.
- Health disk check warned on the empty 512 MB `/tmp` tmpfs of every container.
- Integration tests reusing an external PostgreSQL server left their databases behind (disk filled).
- Dialogs returned focus to the page body; pages scrolled sideways at 360 px (Arabic top bar, wide tables).

## Decisions (ADRs, proposed — for the owner)
0150 setup wizard and setup token · 0151 Health page · 0152 version and release notes · 0153 seed packs and removable demo pack ·
0154 UAE Pass for Wasel vendors · 0155 outbound URL policy (SSRF) · 0156 performance / scanning / SBOM tools (external, never
distributed) · 0157 packaging, install, backup / restore / upgrade · 0158 load testing with k6.

## Deviations
- **From the SPEC:** none known.
- **From the design note** (each recorded in its ADR): permissions `settings.setup.manage` / `settings.health.view` and route
  `/api/v1/settings/health` (permission codes need a system prefix; platform routes are per component); the public setup status does not
  return the version (ASVS 14.3.3); no `--seed-volume` host command (the volume seed bypasses the audit trail, so it stays test-only);
  setup token in `identity.setup_state` (Identity owns users).

## Residual risks (release)
| Risk | Mitigation / plan |
|---|---|
| Windows install scripts executed only under PowerShell 7 on Linux | Run `install.ps1` / `backup.ps1` / `restore.ps1` / `upgrade.ps1` on a Windows machine with Docker Desktop during owner acceptance |
| No real HTTPS reverse proxy with a certificate, no non-Docker install run end to end | nginx configuration and `deploy/linux/README.md` reviewed; verify at the first customer installation |
| No screen-reader walkthrough (NVDA / VoiceOver) | axe + keyboard / focus scripts pass; manual check during acceptance |
| Live MCP check with Claude Desktop / claude.ai / Claude Code over HTTPS (Phase 5) | Procedure in `docs/guides/en/mcp.md`; needs the owner's HTTPS installation |
| `libjbig0` (GPL-2+) among the OCR packages (ADR 0033) | Owner decision; build with `INSTALL_OCR=false` to leave OCR out |
| Sidecars (Gotenberg, ClamAV) unauthenticated on the internal network; images not signed | Loopback / internal network only; SBOM provided; sign when a registry is introduced |
| Composition rules on by default (ASVS 2.1.9); no e-mail on password / MFA change; SSRF check only at save time for SAML metadata, LDAP, SMTP | Configurable / audited; planned for 1.1 |
| Capacity: CPU-bound above ≈ 170 requests / s on 4 vCPU | Sizing guide (8 vCPU beyond ~500 active users) |
| Functional gaps carried into 1.0 (stock-metric comparison on the live path, no global search box, rich text as plain text, KPI picker 200 entries, audit retention purge) | Release notes *Known limitations*; known-gaps review |

## How to demo
```bash
./deploy/install.sh                       # prints http://<host>:8080/setup and the token
```
1. `/setup`: token → organisation → branding → languages → administrator; sign in, enrol MFA; e-mail (test e-mail), providers, AI (skip),
   business hours, import templates, systems, **Demo data** (optional); finish.
2. *Settings → System health*; *About* (version, release notes); *Settings → Demo data* → remove.
3. `./deploy/backup.sh` → `./deploy/restore.sh <folder>` (row counts and audit chain verified); `./deploy/upgrade.sh`.
4. Wasel: switch on UAE Pass for vendors (Settings → Portfolio → Wasel) with a UAE Pass provider; *Sign in with UAE Pass* on `/wasel`.

## Environment notes
- Sandbox: 4 vCPU, 15 GB RAM, shared disk (filled several times by parallel worktrees — test databases and Docker caches pruned);
  Docker through `mirror.gcr.io`; MinIO images unavailable (SeaweedFS used); SDK 10.0.403 copied from the official image (the installer
  download was blocked). Tesseract installed for the OCR tests.
- Screenshots: [`docs/screenshots/phase-18/`](../screenshots/phase-18/) — setup wizard (15 steps × ar / en), health page, vendor UAE Pass
  sign-in (login, no account, MFA, signed in).
