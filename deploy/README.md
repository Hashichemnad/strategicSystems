# deploy/

| File | Purpose |
|---|---|
| `install.sh` / `install.ps1` | One-command install (Linux / Windows Server or Docker Desktop): `.env` with generated passwords, port check, pull the release image (or load the offline archive, or build from source), start, print the setup address and token |
| `backup.sh` / `backup.ps1` | Consistent backup: database (incl. Data Protection keys), files volume, `.env`, optional certificate; manifest with checksums and row counts |
| `restore.sh` / `restore.ps1` | Restore with verification: checksums, row count of every table, audit hash chain |
| `upgrade.sh` / `upgrade.ps1` | In-place upgrade: pre-upgrade backup, new image, migrations as the owner, runtime-role grants, health check, rollback command |
| `lib.sh` / `lib.ps1` | Helpers shared by the scripts |
| `VERSION` | Version of this release folder = image tag `<APP_IMAGE>:<version>` (default `ghcr.io/hashichemnad/strategy-app:<version>`) |
| `docker-compose.yml` | Dev dependencies (`postgres`, `gotenberg`, `mailpit`) and the containerised install (profile `app`, release image). Optional profiles: `search`, `antivirus`, `ai` |
| `docker-compose.build.yml` | Override with the build section: build the image from this source folder (`--build` / `-Build`, ADR 0219) |
| `Dockerfile` | Multi-stage build of the single deployable (SPA + host): non-root, read-only-root friendly, OCI labels, healthcheck |
| `.env.example` | Settings for compose (the installer copies it to `.env`) |
| `sbom.sh` | CycloneDX SBOM of the image (Syft) and `--scan` vulnerability scan (Trivy) — ADR 0156 |
| `nginx/strategy.conf` | HTTPS reverse proxy (forwarded headers, SignalR WebSockets, upload size) |
| `systemd/strategy.service`, `linux/` | Non-Docker install on Linux ([`linux/README.md`](linux/README.md)) |
| `postgres/init-roles.sh` | Creates the runtime database role on the first start of an empty volume (ADR 0018) |
| `empty-ca.pem` | Intentionally empty default for the optional `build_ca` secret (see `BUILD_CA_FILE`) |

Decisions: ADR 0157 (packaging, scripts, backup format), ADR 0219 (release images on ghcr.io, offline archive), ADR 0018 (database roles). Verification of this release:
[`docs/operations/verification-1.0.md`](../docs/operations/verification-1.0.md).

## Requirements

Docker Engine 24+ with Compose v2 (Linux), or Docker Desktop 4.30+ with Linux containers (Windows 10 / 11, Windows Server with WSL 2).
4 vCPU, 8 GB RAM, 50 GB disk for up to ~500 users (building from source needs about 10 GB more, temporarily). Outbound HTTPS to `ghcr.io`
(the application image) and Docker Hub (PostgreSQL, Gotenberg) during install / upgrade, and to the AI provider you configure (none by
default). Offline installs: see *Image* below.

## Install

```bash
./deploy/install.sh                       # Linux
```
```powershell
powershell -ExecutionPolicy Bypass -File deploy\install.ps1     # Windows (PowerShell 5.1 or 7)
```

The installer creates `deploy/.env` with **generated passwords** (kept on later runs; example passwords are refused), checks that the
ports are free, pulls `<APP_IMAGE>:<version>` (or loads `--image <archive>`, or builds with `--build`), starts PostgreSQL, Gotenberg, `migrate` (migrations as the
owner, grants for the runtime role) and the application, waits until it is healthy and prints the address and the **one-time setup token**
for the setup wizard (`/setup`). It is safe to run again.

**Ports.** Only the application is reachable from the network: `APP_PORT` (default 8080) on `APP_BIND` (default all addresses; set
`127.0.0.1` when a reverse proxy on the same host is the only entry). PostgreSQL (`POSTGRES_PORT`, 5432) and Gotenberg (`GOTENBERG_PORT`,
3000) are published on **127.0.0.1 only** — for development and local tools; the application reaches them over the internal network, so
changing them never affects it. When a port is taken the installer stops before starting anything and says which variable to change:

```
  Port 8080 (APP_PORT, the application) is already in use on this machine.
  Choose a free port and run the installer again, for example:
      APP_PORT=8090 ./deploy/install.sh
  or set APP_PORT=<port> in deploy/.env. (Find the program using it: sudo ss -ltnp 'sport = :8080')
```
On Windows: `$env:APP_PORT = 8090; .\deploy\install.ps1`. The chosen ports are saved in `deploy/.env`. Ports held by this installation's own
containers (a second run) are not reported.

**Image (ADR 0219).** Each release publishes `ghcr.io/hashichemnad/strategy-app:<version>` (public package; `:latest` follows the newest
final release) and attaches the same image as an offline archive to the GitHub release. `migrate` and `app` use
`${APP_IMAGE}:${APP_VERSION}` with `pull_policy: missing`: the image is pulled once, and `docker compose … run --rm app …` reuses it.

| Option (`.sh` / `.ps1`) | Image source |
|---|---|
| *(none)* | `docker compose pull migrate app`: `<APP_IMAGE>:<APP_VERSION>` from the registry. A failed pull stops before anything changes and points to `--image`; a copy of the same tag already on the machine is used with a warning |
| `--image <file>` / `-Image <file>` | Offline: `strategy-app-<version>.tar.gz` (or a `.tar` from `docker save`) from the GitHub release. `<file>.sha256` next to it is verified first (a mismatch refuses the archive, nothing is loaded); then `docker load`. An archive with another repository name and the same tag (the ghcr.io name when `APP_IMAGE` is a mirror, or `strategy-app:<version>` from a source build) is tagged as `<APP_IMAGE>:<version>` |
| `--build` / `-Build` | Build from this folder with `docker-compose.build.yml` (build section, `BUILD_CA_FILE`, revision and creation labels) |
| `--no-pull` / `-NoPull` | The image is already on this machine (loaded or pushed by hand). `--no-build` / `-NoBuild` are deprecated aliases |

`APP_IMAGE` (in `deploy/.env`, default `ghcr.io/hashichemnad/strategy-app`) points to a customer registry mirror, e.g.
`registry.example.org/strategy/strategy-app` (mirror the tags with `docker pull` / `docker tag` / `docker push` or the registry's
replication). Download and check an archive on a connected machine:

```bash
curl -LO https://github.com/Hashichemnad/strategicSystems/releases/download/v1.0.2/strategy-app-1.0.2.tar.gz
curl -LO https://github.com/Hashichemnad/strategicSystems/releases/download/v1.0.2/strategy-app-1.0.2.tar.gz.sha256
sha256sum -c strategy-app-1.0.2.tar.gz.sha256      # the scripts check it again on the server
```
For a machine without any Internet access also save `postgres:16-alpine` and `gotenberg/gotenberg:8` (`docker save … | gzip`) and load them
with `docker load -i`. Installations from 1.0.0 / 1.0.1 (image built as `strategy-app:<version>`) need nothing: `upgrade` writes `APP_IMAGE`
to `.env` and tags the old local image with the new name, so the pre-upgrade backup and a rollback still use it.

The manual equivalent of the installer: `cp deploy/.env.example deploy/.env` (change the passwords), then
`docker compose -f deploy/docker-compose.yml --profile app up -d`; from source add `-f deploy/docker-compose.build.yml` and `--build`.

**Windows PowerShell 5.1 and 7, ZIP releases (ADR 0163).** The `.ps1` scripts run unchanged in Windows PowerShell 5.1 (every Windows
Server) and PowerShell 7, also with their output redirected (scheduled task, remote shell, CI): docker's progress on standard error is shown
as plain lines and never stops a script; only exit codes count. Every native command goes through `Invoke-Native` in `lib.ps1`; the scripts
are ASCII-only and write `.env`, `SHA256SUMS`, `manifest.json` and `table-counts.tsv` as UTF-8 without BOM with LF, like the `.sh` twins.
Install and upgrade also work from a release folder **without `.git`** (a ZIP): the image's revision label is then empty; git is only run
when `<release>/.git` exists, and a failing `git rev-parse` means "no revision". The version always comes from `deploy/VERSION`.

**Slow networks (ADR 0163).** Building from source (`--build`) retries downloads with longer timeouts; change the defaults in `deploy/.env`
(build arguments, next build):

| Variable | Default | Effect |
|---|---|---|
| `NPM_FETCH_RETRIES` / `NPM_FETCH_RETRY_MINTIMEOUT` / `NPM_FETCH_RETRY_MAXTIMEOUT` / `NPM_FETCH_TIMEOUT` | 5 / 20000 / 120000 / 600000 (ms) | npm `fetch-retries`, back-off between attempts, time one request may take |
| `NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT` / `NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS` | 10 / 3000 | NuGet's per-request attempts and delay (also on HTTP 429). NuGet's 100 s request timeout itself is not configurable |
| `NUGET_RESTORE_ATTEMPTS` | 3 | whole `dotnet restore` attempts (pause 20 s × attempt; downloaded packages are kept, a retry continues) |
| `NUGET_RESTORE_DISABLE_PARALLEL` | false | `true`: `dotnet restore --disable-parallel` (fewer simultaneous connections) |
| `APT_RETRIES` / `APT_HTTP_TIMEOUT` | 5 / 120 (s) | `Acquire::Retries`, HTTP(S) timeout of `apt-get` in the runtime stage |

Tests: `tests/deploy/test-deploy-scripts.sh` (bash) and `tests/deploy/Test-DeployScripts.ps1` (5.1 / 7) run the scripts against a fake
`docker` in a copy of `deploy/` without `.git` (CI jobs `deploy-scripts` and `deploy-windows`); real Windows with Docker Desktop:
[`docs/operations/windows-acceptance.md`](../docs/operations/windows-acceptance.md).

The container runs as a non-root user with a read-only root file system (writable: `/tmp` as tmpfs and the files volume), no Linux
capabilities and `no-new-privileges`. Logs rotate (`LOG_MAX_SIZE`, `LOG_MAX_FILES`).

### First administrator

**First-run setup wizard (ADR 0150).** While no administrator exists, the application prints a one-time **setup token** in its log at
start-up (the installer shows it; later: `docker compose -f deploy/docker-compose.yml --profile app logs app | grep -o '"SetupToken":"[^"]*"'`,
or look for "FIRST-RUN SETUP"). Open `http://<server>:<APP_PORT>/setup`, enter the token, then the organisation profile, branding, languages
and the first administrator; sign in (two-step verification is set up at the first sign-in) and the guided configuration continues (e-mail
server, sign-in providers, AI, business hours, organisation import, systems). A new token:
`docker compose -f deploy/docker-compose.yml --profile app run --rm app --setup-token`.

Or create the first administrator from the command line (once). It holds the platform administrator roles (ADR 0016). The password is
read from standard input, never from the command line or a file left on disk:

```bash
read -rsp 'Administrator password: ' ADMIN_PASSWORD && echo
printf '%s' "$ADMIN_PASSWORD" | docker compose -f deploy/docker-compose.yml --profile app run --rm -T \
  app --create-admin --email admin@example.org --password-file /dev/stdin
unset ADMIN_PASSWORD
```

`--create-admin` runs as the application's runtime role and never migrates on that connection. Run it after `up` (above) — or after
`docker compose … run --rm migrate` on an upgrade — when the database is up to date; otherwise it stops with "Run --migrate first" and
changes nothing. Run in the `migrate` service instead (`… run --rm -T migrate --create-admin …`), it first applies pending migrations as
the owner. The application runs as a non-root user, so a password file mounted from the host must be readable by that user; standard
input avoids the question. A running application recognises the new administrator's roles within a minute (access cache).

The administrator sets up two-step verification at the first sign-in (required for administrators by default).
Then, in the application (**Settings**): **Email server** (SMTP and the public address used in email links),
**Import** (units, then employees), **Users**, **Role assignments** and **System access** (who may open each system),
and optionally **Identity providers** (see [`docs/identity-providers.md`](../docs/identity-providers.md)).

### HTTPS

Put a TLS reverse proxy in front of the application ([`nginx/strategy.conf`](nginx/strategy.conf)) and set in `deploy/.env`:
`FORWARDED_HEADERS_ENABLED=true`, `FORWARDED_HEADERS_PROXY=<proxy IP as the app sees it>`, `SECURE_COOKIES=true`, and `APP_BIND=127.0.0.1` for a proxy on the
same host.

## Backup

```bash
./deploy/backup.sh                         # → backups/strategy-<version>-<UTC time>/   (Windows: .\deploy\backup.ps1)
./deploy/backup.sh --output /srv/backups
```

The application is stopped for the dump (seconds for small databases) so database, files and row counts are one point in time, and started
again afterwards — also when the backup fails. `--online` skips the stop (the row counts are then informative only). A backup folder holds:

| Part | Content |
|---|---|
| `database.dump` | `pg_dump -Fc` of the whole database as the owner — includes the **Data Protection key ring** (`core.data_protection_keys`), so stored secrets and authenticator keys stay readable |
| `files.tar.gz` | the files volume (`FILES_PROVIDER=local`; with S3 storage back up the bucket with the storage's own tools) |
| `env` | `deploy/.env` (passwords) |
| `dataprotection.pfx` | the Data Protection certificate, when `deploy/secrets/dataprotection.pfx` exists or `--certificate <file>` is given |
| `table-counts.tsv` | exact row count of every table (Quartz runtime tables excluded) |
| `SHA256SUMS`, `manifest.json` | checksums; version, time, sizes, totals, whether the backup is consistent |

The folder is readable by its owner only. Copy it off the machine and protect the copy (it contains passwords and keys). Schedule it with
cron or Task Scheduler, e.g. `15 1 * * * cd /opt/strategy && ./deploy/backup.sh --output /srv/backups >> /var/log/strategy-backup.log 2>&1`.

## Restore (verified)

```bash
./deploy/restore.sh backups/strategy-1.0.0-20261106T021500Z            # asks you to type RESTORE
./deploy/restore.sh <folder> --yes --with-env                          # unattended; also restore deploy/.env
```

Checksums verified **before** anything changes → application stopped → database dropped and restored as the owner → **row count of every
table compared with the backup** (a difference stops the restore; the application is not started) → files restored → `migrate` (upgrades an
older backup's schema, re-grants the runtime role) → **audit hash chain verified** (`--verify-audit-chain`; a broken chain stops the
restore) → application started and health-checked. On a new machine run `install.sh` first (new passwords are fine: the runtime role's
grants come back with the dump), then `restore.sh`. Windows: `.\deploy\restore.ps1 -Backup <folder> [-Yes] [-WithEnv]`. Backups made by
the bash and the PowerShell scripts are interchangeable.

## Upgrade

Unpack the new release folder over the old one (keep `deploy/.env`), then:

```bash
./deploy/upgrade.sh          # pull the new image;  --image <archive> offline;  --build from this folder;  --no-pull when already loaded
```

1. New image `<APP_IMAGE>:<new version>` (pulled, loaded or built while the current version runs; a failure changes nothing).
2. **Pre-upgrade backup** (mandatory; `--no-backup` only right after a backup). 3. Application
stopped. 4. `migrate` as the **owner** (`ConnectionStrings__StrategyMigrations`): every module's migrations, then the runtime role's grants
(`SELECT, INSERT` only on the audit trail). 5. `APP_VERSION` saved in `.env`, application started and health-checked.

**Rollback.** On any failure the script prints the command; the previous image `<APP_IMAGE>:<previous version>` stays on the machine:

```bash
APP_VERSION=<previous version> ./deploy/restore.sh <pre-upgrade backup folder> --yes
```

Read the release notes before upgrading (About in the user menu).

## Publishing a release (maintainers)

`.github/workflows/release.yml` (ADR 0219) runs on a pushed tag `v<version>` (it must equal `deploy/VERSION`): builds `deploy/Dockerfile`
with the version, revision and creation labels, checks non-root / healthcheck / labels, runs the Trivy scan and the SBOM (`sbom.sh --scan`),
pushes `ghcr.io/<owner>/strategy-app:<version>` (and `:latest` unless the version has a pre-release suffix such as `-rc.1`), and attaches
`strategy-app-<version>.tar.gz`, its `.sha256` and the CycloneDX SBOM to the GitHub release `v<version>` (created when missing).
*Run workflow* without `push` is a dry run (assets kept as a workflow artifact for 7 days); with `push: true` it publishes without a pushed
tag. **Once, after the very first push:** GitHub → *Packages* → `strategy-app` → *Package settings* → *Change visibility* → **Public**
(owner decision: anonymous pulls, so customers need no GitHub account or token).

## Non-Docker install

Linux service with PostgreSQL 16, Gotenberg and Tesseract: [`linux/README.md`](linux/README.md) (`systemd/strategy.service`,
`linux/strategy.env.example`; the same `--migrate`, `--create-admin` and `--verify-audit-chain` commands; backup / restore / upgrade by hand).

## Software bill of materials and image scan

```bash
./deploy/sbom.sh            # release/sbom/strategy-app-<version>.cdx.json (CycloneDX, Syft)
./deploy/sbom.sh --scan     # + Trivy: exit 1 on High / Critical vulnerabilities that have a fix
```

## Data Protection keys

Sessions, the TOTP keys and stored provider / SMTP secrets are protected with ASP.NET Core Data Protection.
The key ring is stored in the database (`core.data_protection_keys`), so a database backup contains it: **back up and
restore the database as a whole**, or the stored secrets cannot be read. To encrypt the key ring at rest, mount a
PKCS#12 certificate with a compose override and set `DATAPROTECTION_CERTIFICATE_PATH` / `_PASSWORD` in `.env`:

```yaml
# deploy/docker-compose.override.yml
services:
  app:
    volumes:
      - ./secrets/dataprotection.pfx:/run/secrets/dataprotection.pfx:ro
```

Keep a copy of that certificate with the backups: without it the key ring cannot be decrypted.

## Database roles (ADR 0018)

The application runs as a **runtime role** that may read and write the module schemas but can only **read and append** to the audit trail
(`audit.entry`); migrations run as the **owner** role. New installs get this automatically: `postgres/init-roles.sh` creates the runtime role
(`STRATEGY_APP_USER` / `STRATEGY_APP_PASSWORD` in `.env`) on the first start of an empty volume, `migrate` connects as the owner
(`ConnectionStrings__StrategyMigrations`) and grants the runtime role after every upgrade, and `app` connects as the runtime role.

### Upgrade step: installations created before Phase 3

Older installations ran the application as the owner (`POSTGRES_USER`). Move them to the runtime role once, after taking a backup:

```bash
# 1. In deploy/.env choose the runtime role's name and a strong password (they must differ from POSTGRES_USER):
#      STRATEGY_APP_USER=strategy_app
#      STRATEGY_APP_PASSWORD=<a long random password>
# 2. Pull the new version (or build it: add -f deploy/docker-compose.build.yml and use build migrate), then create the role, upgrade the
#    database and grant the role (idempotent; run it again after a password change):
docker compose -f deploy/docker-compose.yml --profile app pull migrate app
docker compose -f deploy/docker-compose.yml --profile app run --rm migrate --setup-db-roles
# 3. Start the application; it now connects as the runtime role:
docker compose -f deploy/docker-compose.yml --profile app up -d
```

`--setup-db-roles` connects as the owner (`ConnectionStrings__StrategyMigrations`, which needs the `CREATEROLE` privilege — the container's
`POSTGRES_USER` has it), creates the role named in `ConnectionStrings__Strategy` or updates its password, applies pending migrations and
grants: read / write on the module schemas, `SELECT, INSERT` only on `audit.entry`. It exits with code 2 and changes nothing when the two
connection strings are missing or name the same role. Outside Docker, run `dotnet Strategy.Host.dll --setup-db-roles` with both connection
strings set as environment variables.

Until this step is done, the audit trail is still protected by its triggers (no UPDATE, DELETE or TRUNCATE for any role), but the
application role, as owner, could switch them off.

Install, backup, restore and upgrade scripts: see above.

## AI and the MCP server (Phase 5)
- **AI** is off until an administrator configures a provider in **Settings → AI** (Anthropic Claude, OpenAI, Azure OpenAI or a self-hosted
  OpenAI-compatible server such as Ollama / vLLM) and switches it on. Keys are stored encrypted with the Data Protection keys (back them up,
  see above). Customers who may not send data outside their network use a self-hosted model; the optional `ollama` container can host one.
  The host needs outbound HTTPS to the chosen provider only.
- **Text recognition (OCR)** for scanned PDFs and images uses Tesseract with Arabic and English data, installed in the image
  (ADR 0033). Switch it on in Settings → AI → *Data sharing and limits*; the page says whether the engine is installed. To build an image
  without it (release images include it): `INSTALL_OCR=false` in `deploy/.env`, then `install.sh --build` / `upgrade.sh --build` (or
  `docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.build.yml --profile app build --build-arg INSTALL_OCR=false migrate`) — this also leaves out the one GPL-2+ library, `libjbig0` (kept by the owner, ADR 0033; its notice, the GPL-2 text and
  the Ubuntu source reference are in `/app/licenses/ocr/NOTICE-libjbig0-GPL-2.txt`, `GPL-2.txt`, `SOURCES.txt`). Non-Docker installs: install `tesseract-ocr`, `tesseract-ocr-ara`,
  `tesseract-ocr-eng` (or point `Ocr__TesseractPath` to the binary). The licence notices of every package the OCR install adds are in
  `/app/licenses/ocr/` in the image (with `packages.txt`); PDFium's and SkiaSharp's are in `/app/licenses/`. Scanned PDF pages are rendered
  with PDFium, which ships inside the application (no extra package).
- **MCP server** (`/mcp`) is off until switched on in **Settings → MCP**. Its OAuth sign-in requires **HTTPS** (`MCP_REQUIRE_HTTPS=true`,
  the default). Put the application behind a TLS reverse proxy and let it see the original address:
  ```
  # deploy/.env
  FORWARDED_HEADERS_ENABLED=true
  FORWARDED_HEADERS_PROXY=<IP address of the reverse proxy as the app sees it>
  ```
  Without `FORWARDED_HEADERS_PROXY` every sender is trusted: do that only when the app port is reachable by the proxy alone.
  How users connect Claude: [`docs/mcp-guide.md`](../docs/mcp-guide.md).
