# Installation guide — Integrated Strategy System 1.0

For the IT team installing the system on the organisation's own servers. Everything runs inside your network; no licence key, no
connection to the vendor and no internet access are needed (only the AI provider you choose, if any).

> Reference material for operators: [`deploy/README.md`](../../../deploy/README.md) (every script and option),
> [`deploy/linux/README.md`](../../../deploy/linux/README.md) (non-Docker install), and the verification record
> [`docs/operations/verification-1.0.md`](../../operations/verification-1.0.md).

## 1. What you install

| Part | What it does | Where it runs |
|---|---|---|
| **Application** (`strategy-app:1.0.0`) | The web application, API, MCP server and scheduled jobs — one program | container (or a Linux service) |
| **PostgreSQL 16** | All data, including the encryption keys of stored secrets (Data Protection key ring) | container (or your database server) |
| **Gotenberg 8** | Turns documents into PDF with correct Arabic shaping | container |
| Optional: ClamAV, Meilisearch, Ollama | Virus scanning of uploads, search inside documents, a local AI model | containers (`--profile antivirus` / `search` / `ai`) |

## 2. Requirements

| Item | Minimum (up to ~500 users) | Larger installations |
|---|---|---|
| Server | 4 vCPU, 8 GB RAM, 50 GB SSD | 8 vCPU, 16 GB RAM; keep 20 % disk free |
| Linux | Docker Engine 24+ with Compose v2 (Ubuntu 22.04 / 24.04, Debian 12, RHEL 9) | — |
| Windows | Windows Server 2022 / Windows 10–11 with **Docker Desktop 4.30+** (Linux containers, WSL 2); PowerShell 5.1 or 7 | — |
| Building the image | about 10 GB free disk during the build | — |
| Network | users reach the application port (or the HTTPS proxy); outbound HTTPS only to the AI provider you configure and your SMTP server | — |

**Text recognition (OCR).** The image includes Tesseract OCR with Arabic and English data for scanned documents (switched on in
*Settings → AI*). The OCR packages include one GPL-2+ library, `libjbig0`; its notice, the GPL-2 text and the source reference are in the
image under `/app/licenses/ocr/` (also listed in `docs/THIRD_PARTY_LICENSES.md`). To build an image **without** the OCR packages (and without
`libjbig0`; about 40 MB smaller, scans can then not be read), set `INSTALL_OCR=false` in `deploy/.env` before running the installer, or
build by hand with `docker compose -f deploy/docker-compose.yml --profile app build --build-arg INSTALL_OCR=false`.

**Offline or restricted networks.** On a connected machine: `docker save strategy-app:1.0.0 -o strategy-app-1.0.0.tar`, and save
`postgres:16-alpine` and `gotenberg/gotenberg:8` the same way. On the server: `docker load -i …` for each, then install with
`--image strategy-app-1.0.0.tar` (below). Behind a Docker Hub limit set `REGISTRY_PREFIX=mirror.gcr.io/` (or your own registry mirror);
behind a TLS-inspecting proxy point `BUILD_CA_FILE` to your organisation's CA bundle (used only while building, never stored in the image).

**Slow or unreliable connections.** The image build downloads npm and NuGet packages and Ubuntu updates. It already retries with
longer timeouts; on a very slow line raise the values in `deploy/.env` before running the installer (they take effect on the next build):

| Setting in `deploy/.env` | Default | What it changes |
|---|---|---|
| `NPM_FETCH_RETRIES` | 5 | attempts per npm package |
| `NPM_FETCH_RETRY_MINTIMEOUT` / `NPM_FETCH_RETRY_MAXTIMEOUT` | 20000 / 120000 | shortest / longest pause between npm attempts (milliseconds) |
| `NPM_FETCH_TIMEOUT` | 600000 | how long one npm request may take (milliseconds) |
| `NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT` / `NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS` | 10 / 3000 | attempts per NuGet request and the pause between them (ms) |
| `NUGET_RESTORE_ATTEMPTS` | 3 | how often the whole .NET package restore is retried (packages already downloaded are kept) |
| `NUGET_RESTORE_DISABLE_PARALLEL` | false | `true`: one project at a time (fewer simultaneous connections) |
| `APT_RETRIES` / `APT_HTTP_TIMEOUT` | 5 / 120 | attempts and timeout (seconds) for the Ubuntu packages of the runtime image |

For example `NUGET_RESTORE_ATTEMPTS=6` and `NPM_FETCH_RETRIES=10`. A connection that fails completely still fails the build — then use
the offline install above.

## 3. Install with Docker (recommended)

Unpack the release folder, for example to `/opt/strategy` (Linux) or `C:\strategy` (Windows), and run **one command** from that folder.
A ZIP of the release without a `.git` folder is fine (the image then has no commit label; the version comes from `deploy/VERSION`). On
Windows the scripts run in **Windows PowerShell 5.1** (built into every Windows Server) and in **PowerShell 7**, also with their output
redirected to a log file (`... *> install.log`):

```bash
./deploy/install.sh
```
```powershell
powershell -ExecutionPolicy Bypass -File deploy\install.ps1
```

The installer:
1. creates `deploy/.env` with **generated passwords** (kept on later runs; the example passwords are refused);
2. checks that the ports are free (see §4);
3. builds the image `strategy-app:1.0.0` — or loads it with `--image <archive>` / `-Image <archive>`;
4. starts PostgreSQL, Gotenberg, the one-off `migrate` step (creates the database objects as the owner role and grants the runtime role)
   and the application;
5. waits until the application is healthy and prints its address and the **one-time setup token**.

Running it again is safe: existing passwords and data are kept and the containers are updated in place.

## 4. Ports already in use

Only the application port is reachable from the network. PostgreSQL and Gotenberg are published on `127.0.0.1` only (for local tools).

| Variable | Default | Used by |
|---|---|---|
| `APP_PORT` | 8080 | the application (users or the HTTPS proxy) |
| `APP_BIND` | all addresses | set `127.0.0.1` when the HTTPS proxy runs on the same server |
| `POSTGRES_PORT` | 5432 | PostgreSQL on 127.0.0.1 |
| `GOTENBERG_PORT` | 3000 | Gotenberg on 127.0.0.1 |

When a port is taken the installer stops **before starting anything** and names the variable to change:

```
  Port 8080 (APP_PORT, the application) is already in use on this machine.
  Choose a free port and run the installer again, for example:
      APP_PORT=8090 ./deploy/install.sh
```

Windows: `$env:APP_PORT = 8090; .\deploy\install.ps1`. The chosen ports are saved in `deploy/.env`, so later commands use them too.
To find the program holding a port: `sudo ss -ltnp 'sport = :8080'` (Linux) or `Get-NetTCPConnection -LocalPort 8080` (Windows).

## 5. First start: the setup wizard

Open `http://<server>:<APP_PORT>/setup` and enter the setup token the installer printed. Lost it? Print a new one:

```bash
docker compose -f deploy/docker-compose.yml --profile app run --rm app --setup-token
```

**Stage 1 (with the token):** organisation profile (name in Arabic and English, time zone, currency), branding (colour, e-mail header and
footer), languages (default language, Hijri dates, Arabic-Indic digits) and the **first administrator**. The token then expires; nobody
can use the wizard again.

**Stage 2 (signed in):** sign in as the administrator and set up two-step verification (required for administrators). The wizard continues:
e-mail server (with a test e-mail), sign-in provider (optional — Entra ID / ADFS / UAE Pass / SAML / LDAP), AI provider (optional, can be
skipped), business hours and holidays, organisation-structure import (download the templates, fill units then employees, upload), the
systems to enable, and optional **demo data** for presentations (removable later in *Settings → Demo data*). Every step can be skipped
and done later in *Settings*; the wizard stays available in *Settings → Setup wizard* until you finish it.

Scripted alternative (no wizard): create the administrator from the command line — the password is read from standard input:

```bash
read -rsp 'Administrator password: ' P && echo
printf '%s' "$P" | docker compose -f deploy/docker-compose.yml --profile app run --rm -T app --create-admin --email admin@example.org --password-file /dev/stdin
unset P
```

## 6. HTTPS (required for production and for the MCP server)

Put a TLS reverse proxy in front of the application. A ready configuration for nginx is in
[`deploy/nginx/strategy.conf`](../../../deploy/nginx/strategy.conf) (forwarded headers, WebSockets for live notifications, upload size).
Then set in `deploy/.env` and run the installer again:

```
FORWARDED_HEADERS_ENABLED=true
FORWARDED_HEADERS_PROXY=<IP address of the proxy as the application sees it>
SECURE_COOKIES=true
APP_BIND=127.0.0.1          # when the proxy runs on the same server
```

Use your organisation's certificate. *Settings → System health* can warn before it expires when the certificate file is mounted and listed in
`Health:CertificatePaths`.

## 7. Backup

```bash
./deploy/backup.sh                          # Windows: .\deploy\backup.ps1
./deploy/backup.sh --output /srv/backups
```

A backup folder `backups/strategy-<version>-<UTC time>/` holds the database (**including the encryption keys** of stored secrets and
authenticator keys), the files, `deploy/.env`, the optional Data Protection certificate, the row count of every table and checksums. The
application stops for a few seconds so everything is from one moment (`--online` avoids the stop). The folder contains passwords and keys:
copy it off the server and protect the copy. Schedule it daily, for example with cron:

```
15 1 * * * cd /opt/strategy && ./deploy/backup.sh --output /srv/backups >> /var/log/strategy-backup.log 2>&1
```

With S3-compatible file storage, back up the bucket with the storage's own tools.

## 8. Restore (with verification)

```bash
./deploy/restore.sh backups/strategy-1.0.0-20261106T021500Z         # asks you to type RESTORE
```
Windows: `.\deploy\restore.ps1 -Backup <folder>`. The script checks the checksums **before** changing anything, restores the database and
files, compares the **row count of every table** with the backup, upgrades the schema if the backup is older, re-grants the runtime role,
**verifies the audit trail's hash chain**, then starts and health-checks the application. Any mismatch stops the restore. On a new server:
run the installer first, then the restore (add `--with-env` to bring back the old `deploy/.env`).

## 9. Upgrade

Unpack the new release over the old folder (keep `deploy/.env`), read the release notes, then:

```bash
./deploy/upgrade.sh                         # Windows: .\deploy\upgrade.ps1     offline: --image <archive>
```

The script builds the new image while the old version still runs, takes the **pre-upgrade backup**, stops the application, applies the
migrations as the owner role, re-grants the runtime role, starts the new version and checks its health. If anything fails it prints the
rollback command, for example:

```bash
APP_VERSION=0.17.0 ./deploy/restore.sh <pre-upgrade backup folder> --yes
```

After an upgrade from a build before 1.0: scheduled reminders, digests and escalations now run on their schedules — tell your users.

## 10. Install without Docker (Linux service)

Supported alternative: the .NET 10 runtime, PostgreSQL 16, Gotenberg (as a container or binary), optionally Tesseract OCR
(`tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng`), the systemd unit `deploy/systemd/strategy.service` and nginx. Step-by-step
instructions, including the two database roles, the first migration, backup, restore and upgrade by hand:
[`deploy/linux/README.md`](../../../deploy/linux/README.md).

## 11. After installation

* *Settings → System health* — database, migrations, scheduled jobs, outbox, e-mail, AI, OCR, storage, disk space, certificate expiry, audit chain,
  version. Check it after every installation, restore and upgrade.
* *About* in the user menu — version and release notes.
* Administrator tasks are in the [administrator guide](administration.md); connecting Claude or another MCP client is in the
  [MCP guide](mcp.md).

## 12. Troubleshooting

| Symptom | What to do |
|---|---|
| Installer: "port … is already in use" | choose another port (§4) |
| `/setup` says the wizard is closed | an administrator already exists — sign in, or use *Forgot password* |
| Setup token refused | tokens expire after 24 hours and after a restart; print a new one (§5) |
| "Run --migrate first" from `--create-admin` | run `docker compose -f deploy/docker-compose.yml --profile app run --rm migrate`, then retry |
| Image build fails on certificates | set `BUILD_CA_FILE` to your proxy's CA bundle |
| Image build fails with timeouts (`ETIMEDOUT`, `ECONNRESET`, `NU1301`, "Unable to load the service index") | raise the retry settings in `deploy/.env` (§2, *Slow or unreliable connections*) and run the installer again (steps already completed are taken from Docker's build cache) |
| Docker Hub "too many requests" | set `REGISTRY_PREFIX=mirror.gcr.io/` (or your mirror) in `deploy/.env` |
| PDFs fail | *Settings → System health* → PDF service; check the `gotenberg` container (`docker compose … ps`) |
| E-mails not sent | *Settings → E-mail server* → *Send test e-mail*; *Settings → System health* → outbox |
| Logs | `docker compose -f deploy/docker-compose.yml --profile app logs app` (structured JSON; secrets are never logged) |
