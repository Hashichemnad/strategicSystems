# 0157 — Packaging, install, backup / restore / upgrade format

- **Status:** Accepted by the owner (Phase 18)
- **Context:** SPEC §0.0 (single-tenant, self-hosted, restricted / offline networks) and §0.5 (*guided installer*, *Docker-based deployment
  plus plain server install*, *versioned migrations and an in-place upgrade path*, *backup / restore tooling*). Until Phase 17 an install was
  a manual sequence from `deploy/README.md` and there were no backup, restore or upgrade tools. Customers run Linux servers, Windows Server
  and Windows workstations with Docker Desktop; some have no Internet access.

## Decision

### Image and compose
- One image `strategy-app:<version>`; the version comes from `deploy/VERSION` (scripts) and is written to `APP_VERSION` in `deploy/.env`.
  OCI labels: title, description, version, revision (git commit), created, licenses, source, base name. Runtime user is the base image's
  non-root `app` user (uid 1654); `/app` stays root-owned (not writable by the process); healthcheck `--health-check` (no curl).
- Compose runs the app with a **read-only root file system** (`read_only`, `tmpfs /tmp`, `TMPDIR=/tmp`), `no-new-privileges`, all
  capabilities dropped. Writable: `/tmp` (tmpfs) and the files volume. Data Protection keys are in the database.
- `migrate` and `app` use `pull_policy: never`; `migrate` has the build section, so the image is **built only when the tag is missing**
  (or with `--build`) and never pulled. Phase 17 used `pull_policy: build`, which rebuilt the whole image on every
  `docker compose run … app --create-admin` / `run migrate` (minutes and gigabytes of build cache each time).
- Only the application is published on the network (`APP_BIND:APP_PORT`, default `0.0.0.0:8080`). PostgreSQL and Gotenberg are published
  on **loopback only** (`127.0.0.1:POSTGRES_PORT` / `GOTENBERG_PORT`): reachable for development (the CLAUDE.md dev command is unchanged)
  and local administration tools, never from the network; the app uses the internal compose network, so these ports can be changed freely.
- Logs: `json-file` with rotation (`LOG_MAX_SIZE` 20 MB × `LOG_MAX_FILES` 5) on every service. Healthchecks: postgres (`pg_isready`),
  gotenberg (`/health`), app (image healthcheck).

### Scripts (`deploy/`, bash and PowerShell with the same behaviour and file formats)
| Script | Behaviour |
|---|---|
| `install.sh` / `install.ps1` | Checks Docker + Compose v2; creates `deploy/.env` from `.env.example` with **generated passwords** (48 hex chars from the OS RNG) when absent, refuses example passwords otherwise; checks `APP_PORT` / `POSTGRES_PORT` / `GOTENBERG_PORT` (a port held by this installation's own containers is not "in use") and stops with a message naming the variable and the command to change it; then saves the given ports; builds (`compose build migrate`) or loads an image archive (`--image`, offline installs); `up -d`; waits for the app healthcheck; prints the address and the one-time **setup token** from the app log (line `Setup token:`), or the `--create-admin` command when none is printed. Re-runnable |
| `backup.sh` / `backup.ps1` | Stops the app (default; `--online` keeps it running and marks the manifest `"consistent": false`), `pg_dump -Fc` as the owner (includes `core.data_protection_keys`), exact row count of every table (`table-counts.tsv`, Quartz runtime tables excluded), `tar.gz` of the files volume streamed out of the app image (S3 storage: the bucket is backed up with the storage's tools), copy of `deploy/.env` and of `deploy/secrets/dataprotection.pfx` (or `--certificate`), starts the app again (also on failure), `SHA256SUMS` and `manifest.json` (format 1, version, UTC time, parts with sha256 and size, table and row totals). Folder `backups/strategy-<version>-<UTC>/`, mode 700 (Windows: ACL current user + Administrators) |
| `restore.sh` / `restore.ps1` | Verifies the checksums **before** touching anything; asks for `RESTORE` (or `--yes`); stops the app; `DROP DATABASE … WITH (FORCE)` / `CREATE DATABASE` / `pg_restore --exit-on-error` as the owner; compares every table's row count with the backup (any difference → stop, app not started); restores files; runs `migrate` (upgrades an older backup's schema, re-grants the runtime role); `--verify-audit-chain` (full hash-chain walk, exit 4 when broken → app not started); starts the app and waits for health. `--with-env` also restores `deploy/.env`; `APP_VERSION=<x>` chooses the image (rollback) |
| `upgrade.sh` / `upgrade.ps1` | Pre-upgrade backup (mandatory unless `--no-backup`); new image (build, `--image` archive, or `--no-build` when already loaded); stop app; `migrate` (owner role, grants); `APP_VERSION` saved only after migrations succeed; start; health wait. On any failure prints the rollback command: `APP_VERSION=<old> ./deploy/restore.sh <pre-upgrade backup> --yes` (the old image stays on the machine) |

- The runtime database role is a **cluster** role: on a new volume `init-roles.sh` creates it with the new installation's password; its
  grants come back with the dump. Restoring onto a new machine therefore works with that machine's own `deploy/.env` (tested), or with the
  backup's (`--with-env`).
- `--verify-audit-chain` (host command): full verification through the audit component's verifier (`IAuditChainVerification`), recorded as
  a verification run like the screen's *Verify now*; exit 0 intact, 3 database not migrated, 4 broken.
- Non-Docker install: `deploy/systemd/strategy.service` (hardened unit), `deploy/linux/README.md` (PostgreSQL 16 roles with the SQL of
  `init-roles.sh`, Gotenberg as a container, Tesseract packages, `--migrate` as the owner, `--create-admin`), `deploy/nginx/strategy.conf`
  (TLS reverse proxy, forwarded headers, SignalR WebSockets, upload size). Backup outside Docker: the same parts by hand (`pg_dump -Fc`,
  tar of `Files__LocalPath`, the environment file), documented.

## Consequences
- One documented, tested path for install, backup, restore (incl. verification) and upgrade with rollback, on Linux and Windows.
- A consistent backup costs a short downtime (seconds for small databases); `--online` is available but then counts are not a check.
- The scripts depend only on Docker, bash 4 / PowerShell 5.1+, coreutils (`sha256sum`, `od`), no extra tools in the images.
- Backups contain secrets (`env`, key ring, certificate): the folder is created private; operators must store copies protected.
