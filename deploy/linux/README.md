# Non-Docker install on Linux (systemd)

The supported alternative to the Docker install (SPEC §0.5, ADR 0157). Tested on Ubuntu 24.04 / Debian 12; any distribution with the
.NET 10 runtime and PostgreSQL 16 works. All commands as root unless noted.

## 1. Requirements

| Component | Version | Notes |
|---|---|---|
| CPU / RAM / disk | 4 vCPU, 8 GB RAM, 50 GB SSD (up to ~500 users); 8 vCPU / 16 GB beyond | files grow with documents; keep 20 % free |
| ASP.NET Core runtime | 10.0 | `apt-get install -y aspnetcore-runtime-10.0` (Microsoft package feed) |
| PostgreSQL | 16 or newer | local or a managed server; `pg_dump` / `pg_restore` of the same major version for backups |
| Gotenberg | 8 | PDF rendering (headless Chromium). Simplest as a container: `docker run -d --restart unless-stopped -p 127.0.0.1:3000:3000 gotenberg/gotenberg:8` |
| Tesseract OCR (optional) | 5 | `apt-get install -y tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng` — scanned documents (ADR 0033) |
| libldap | 2.5+ | only for LDAP / Active Directory sign-in: `apt-get install -y libldap2` (or `libldap-2.5-0`) |
| nginx | 1.18+ | TLS reverse proxy: `deploy/nginx/strategy.conf` |

## 2. Database and roles

```bash
sudo -u postgres psql <<'SQL'
CREATE ROLE strategy LOGIN PASSWORD '<owner password>' CREATEROLE;      -- owner: runs migrations
CREATE DATABASE strategy OWNER strategy;
CREATE ROLE strategy_app LOGIN PASSWORD '<runtime password>';             -- runtime role (ADR 0018)
GRANT CONNECT, TEMPORARY ON DATABASE strategy TO strategy_app;
SQL
```
This is the SQL of `deploy/postgres/init-roles.sh`. The grants on the schemas (read / write, `SELECT, INSERT` only on `audit.entry`) are
applied by `--migrate` after every migration.

## 3. Application files

Build the release (on a build machine with the .NET 10 SDK and Node 22) or unpack the release archive:

```bash
dotnet publish src/Host/Strategy.Host/Strategy.Host.csproj -c Release -o out/app -p:UseAppHost=false
(cd web && npm ci && npm run build) && cp -r web/dist out/app/wwwroot
```

```bash
useradd --system --home /opt/strategy --shell /usr/sbin/nologin strategy
mkdir -p /opt/strategy /var/lib/strategy/files /var/tmp/strategy /etc/strategy
cp -r out/app /opt/strategy/app                     # application files stay owned by root (read-only for the service)
chown -R strategy:strategy /var/lib/strategy /var/tmp/strategy
cp deploy/linux/strategy.env.example /etc/strategy/strategy.env   # edit: passwords, URLs
chown root:strategy /etc/strategy/strategy.env && chmod 640 /etc/strategy/strategy.env
```

## 4. Migrate, first administrator, service

```bash
cd /opt/strategy/app
# Migrations as the owner (also grants the runtime role). Repeat on every upgrade.
set -a; . /etc/strategy/strategy.env; set +a
ConnectionStrings__StrategyMigrations='Host=127.0.0.1;Port=5432;Database=strategy;Username=strategy;Password=<owner password>' \
  sudo -E -u strategy dotnet Strategy.Host.dll --migrate

cp /opt/strategy/source/deploy/systemd/strategy.service /etc/systemd/system/   # or from the release folder
systemctl daemon-reload && systemctl enable --now strategy
journalctl -u strategy -f          # the one-time setup token is printed here ("Setup token: …"); open https://<host>/setup
```

Instead of the setup wizard, the first administrator can be created from the command line (password on standard input):

```bash
read -rsp 'Administrator password: ' P && echo && printf '%s' "$P" | \
  sudo -E -u strategy dotnet Strategy.Host.dll --create-admin --email admin@example.org --password-file /dev/stdin; unset P
```

## 5. Reverse proxy

`deploy/nginx/strategy.conf` (HTTPS, forwarded headers, SignalR WebSockets, upload size). The service listens on `127.0.0.1:8080` only.

## 6. Backup, restore, upgrade (without Docker)

| Task | Commands |
|---|---|
| Backup | `systemctl stop strategy` · `pg_dump -Fc -U strategy -h 127.0.0.1 strategy > database.dump` · `tar czf files.tar.gz -C /var/lib/strategy/files .` · copy `/etc/strategy/strategy.env` (and the Data Protection certificate) · `sha256sum * > SHA256SUMS` · `systemctl start strategy` |
| Restore | `systemctl stop strategy` · `sha256sum -c SHA256SUMS` · `dropdb --force strategy && createdb -O strategy strategy` · `pg_restore -U strategy -d strategy --exit-on-error database.dump` · restore files · `--migrate` (as in §4) · `sudo -E -u strategy dotnet Strategy.Host.dll --verify-audit-chain` (exit 0) · `systemctl start strategy` |
| Upgrade | backup · stop · replace `/opt/strategy/app` with the new release (keep the old folder for rollback) · `--migrate` as the owner · start · check *Settings → Health*. Rollback: stop, restore the backup, put the old folder back, start |

The database dump contains the Data Protection key ring (`core.data_protection_keys`): always restore the database as a whole.
