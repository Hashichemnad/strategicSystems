# deploy/

| File | Purpose |
|---|---|
| `docker-compose.yml` | Dev dependencies (`postgres`, `gotenberg`, `mailpit`) and a containerised install (profile `app`). Optional profiles: `search`, `antivirus`, `ai`. |
| `Dockerfile` | Multi-stage build of the single deployable (SPA + host), non-root runtime, built-in healthcheck. |
| `.env.example` | Settings for compose. Copy to `.env`. |
| `empty-ca.pem` | Intentionally empty default for the optional `build_ca` secret (see `BUILD_CA_FILE`). |

## First start

```bash
cp deploy/.env.example deploy/.env          # change the passwords
docker compose -f deploy/docker-compose.yml --profile app up -d --build
```

The image `strategy-app` is **built locally, never pulled**: the `migrate` service builds it (`pull_policy: build`) and the `app` service
reuses it (`pull_policy: never`), so the command above works on a fresh machine (no "pull access denied for strategy-app"). `APP_VERSION`
only tags the locally built image. `migrate` applies the migrations as the database owner, grants the runtime role (ADR 0018) and exits;
`app` starts after it.

Create the first administrator (once). It holds the platform administrator roles (ADR 0016). The password is read from standard input,
never from the command line or a file left on disk:

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
input avoids the question.

The administrator sets up two-step verification at the first sign-in (required for administrators by default).
Then, in the application (**Settings**): **Email server** (SMTP and the public address used in email links),
**Import** (units, then employees), **Users**, **Role assignments** and **System access** (who may open each system),
and optionally **Identity providers** (see [`docs/identity-providers.md`](../docs/identity-providers.md)).

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
# 2. Pull / build the new version, then create the role, upgrade the database and grant the role (idempotent; run it again after a
#    password change):
docker compose -f deploy/docker-compose.yml --profile app build
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

Install, backup and upgrade scripts arrive in Phase 18.

## AI and the MCP server (Phase 5)
- **AI** is off until an administrator configures a provider in **Settings → AI** (Anthropic Claude, OpenAI, Azure OpenAI or a self-hosted
  OpenAI-compatible server such as Ollama / vLLM) and switches it on. Keys are stored encrypted with the Data Protection keys (back them up,
  see above). Customers who may not send data outside their network use a self-hosted model; the optional `ollama` container can host one.
  The host needs outbound HTTPS to the chosen provider only.
- **Text recognition (OCR)** for scanned PDFs and images uses Tesseract with Arabic and English data, installed in the image
  (ADR 0033). Switch it on in Settings → AI → *Data sharing and limits*; the page says whether the engine is installed. To build an image
  without it: `docker compose … build --build-arg INSTALL_OCR=false`. Non-Docker installs: install `tesseract-ocr`, `tesseract-ocr-ara`,
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
