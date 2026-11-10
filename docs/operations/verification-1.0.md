# Release 1.0.0 — verification of install, backup / restore and upgrade

Run on 6 Nov 2026 in the build sandbox (Linux, Docker Engine 29.9, Compose v2; bash scripts and the PowerShell scripts under PowerShell 7
on the same machine). ADR 0157. Image `strategy-app:1.0.0` built from commit `9bb55ef` (merged release branch); Phase 17 image
`strategy-app:0.17.0` built from `939d560`.

> Disk in the sandbox was too small for the multi-stage build's cache (~7 GB), so the verification images were built with the Dockerfile's
> **own runtime stage** on top of `dotnet publish` / `npm run build` output from the same commits (same runtime content). CI builds the full
> multi-stage Dockerfile (`docker` job).

## Results

| # | Check | Command | Result |
|---|---|---|---|
| 1 | Port already in use | `APP_PORT=18090 … ./deploy/install.sh` with another program on 18090 | exit 3 before anything starts: "Port 18090 (APP_PORT, the application) is already in use … `APP_PORT=18100 ./deploy/install.sh` … or set APP_PORT in deploy/.env"; 0 containers created |
| 2 | Fresh offline install | `docker save` → `docker rmi` → `./deploy/install.sh --image strategy-app-1.0.0.tar` on empty volumes | exit 0; `.env` with generated passwords; postgres / gotenberg / migrate / app healthy; installer printed the address and the one-time setup token |
| 3 | Setup token | `POST /api/v1/setup/verify-token` | wrong token 403 · printed token 204 · `GET /setup/status` → `needsSetup: true` |
| 4 | Setup wizard bootstrap | `POST /api/v1/setup/bootstrap` with the token (organisation, branding, languages, first administrator) | 200, administrator created; status → `needsSetup: false`; the same token again → 403 (consumed) |
| 5 | First administrator signs in | `POST /api/v1/identity/auth/login` | 200 `mfaEnrolmentRequired` (TOTP / e-mail) — MFA enforced for administrators |
| 6 | Upgrade from 0.17.0 with data | Phase 17 install (administrator, 5 employees with photos in the files volume, audit trail) → `./deploy/upgrade.sh --no-build` | exit 0 in 46 s: pre-upgrade backup (228 tables, 3 244 rows), migrations as owner, app 1.0.0 healthy; employees and photos intact; `--verify-audit-chain` intact (1 578 entries) |
| 7 | Rollback to 0.17.0 | `APP_VERSION=0.17.0 ./deploy/restore.sh <pre-upgrade backup> --yes` | exit 0: row counts identical (228 tables, 3 244 rows), app 0.17.0 healthy, data intact; chain check left to the screen (0.17.0 has no `--verify-audit-chain`; the script says so) |
| 8 | Upgrade again with PowerShell | `pwsh deploy/upgrade.ps1 -NoBuild` | exit 0; image built / loaded before the backup, app kept stopped between backup and migrations; data intact; chain intact (1 589 entries) |
| 9 | Backup → wipe → restore on a new install | `backup.sh` → `compose down -v` (all volumes deleted) → `install.sh` (new generated passwords) → `pwsh restore.ps1 -Backup … -Yes` | exit 0: checksums OK, 229 tables / 3 130 rows identical, files restored, migrate + grants, audit chain intact (1 544 entries), app healthy; the administrator from the backup signs in (MFA enrolment) — the restored database works with the new machine's runtime-role password |
| 10 | Image scan | `./deploy/sbom.sh --scan` (Trivy 0.76, HIGH / CRITICAL with a fix) | 0 (0.17.0 image: 2 High in `apt`, fixed by the runtime stage's `apt-get upgrade`) |
| 11 | SBOM | `./deploy/sbom.sh` (Syft 1.57, CycloneDX JSON) | `release/sbom/strategy-app-1.0.0.cdx.json`, 4 645 components |
| 12 | Image facts | `docker image inspect` | user `app` (non-root), healthcheck, OCI labels (title, version 1.0.0, revision); 606 MB (Phase 17: 1.35 GB — publish for the image's architecture only) |
| 13 | Script syntax | `shellcheck -x deploy/*.sh`, `tools/check-powershell.ps1 deploy` | clean |

## Full multi-stage build and clean install (main thread, after all streams were merged)

| # | Check | Result |
|---|---|---|
| 14 | `./deploy/install.sh` from a fresh clone (commit `5f271d4`), **full multi-stage `docker build`** (SPA + publish + runtime), empty volumes, ports 8095 / 5442 / 3010 | first attempt **failed**: SDK 10.0.403 in `mcr.microsoft.com/dotnet/sdk:10.0` flagged an unused `using` (IDE0005) that the older local SDK (10.0.114) did not — fixed in `5f271d4`; the local SDK was switched to 10.0.403 (copied from the image). Second run: exit 0, image `strategy-app:1.0.0` 607 MB, user 1654 (non-root), healthy, label version 1.0.0 |
| 15 | Setup token on that container | wrong 403 · printed 204 · bootstrap 200 · reuse 403 · status `needsSetup: false` |
| 16 | First administrator | login 200 `mfaEnrolmentRequired` → TOTP enrolment → `signedIn` with 10 recovery codes |
| 17 | *Settings → System health* as that administrator | 200; database, migrations, scheduler, outbox, PDF, storage, Data Protection, version **ok**; e-mail / AI / OCR / antivirus / certificates **not configured** (fresh install); audit chain **warning** (never verified yet — the daily job has not run); disk **error** (the sandbox disk was 94 % full — correct). Found and fixed: the empty 512 MB `/tmp` tmpfs was reported as a warning (absolute 2 GB floor) — floors are now capped for small volumes |
| 18 | Security headers on `/` | CSP `default-src 'self' …`, `X-Frame-Options: DENY`, `Referrer-Policy`, no `Server` header |

## Release image

| # | Check | Result |
|---|---|---|
| 19 | Final image `strategy-app:1.0.0` built from `673e092` (full multi-stage Dockerfile) | 607 MB; SBOM regenerated (`release/sbom/strategy-app-1.0.0.cdx.json`, 4 645 components); Trivy 0.76.2: **0** High / Critical with a fix (OS packages and .NET dependencies clean) |
| 20 | Backup → delete all volumes → new install (new passwords) → `restore.sh` on that image | restore verified: 230 tables / 3 115 rows identical, audit chain intact (1 540 entries); screenshots `docs/screenshots/phase-18/*-ops-*.png` |

## Not verified here
- **Windows with Docker Desktop**: the PowerShell scripts were run with PowerShell 7 on Linux (same compose, same Docker). Windows
  specifics handled in the scripts but not executed on Windows: PowerShell 5.1 quoting of native arguments (SQL passed on standard input),
  `Get-NetTCPConnection` port check, ACL of the backup folder. To be confirmed on the owner's Windows machine (installation guide steps).
  *Phase 19 (ADR 0163):* the scripts now also run under Windows PowerShell 5.1 (native stderr no longer stops them, UTF-8 files) and from
  a release folder without `.git`; CI runs them against a fake `docker` under 5.1 and 7 (`deploy-windows`). The real-machine run is the
  checklist [`windows-acceptance.md`](windows-acceptance.md).
- An HTTPS reverse proxy with a real certificate (nginx configuration reviewed).
- Non-Docker install (`deploy/linux/README.md`): commands reviewed, not executed end to end.
