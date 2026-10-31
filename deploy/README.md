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

# Create the first administrator (once): holds the platform administrator roles (ADR 0016). The password is read from a file, never from the command line.
printf '%s' 'a-strong-password' > /tmp/admin-password && chmod 600 /tmp/admin-password
docker compose -f deploy/docker-compose.yml --profile app run --rm \
  -v /tmp/admin-password:/run/secrets/admin-password:ro \
  app --create-admin --email admin@example.org --password-file /run/secrets/admin-password
rm /tmp/admin-password
```

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

Install, backup and upgrade scripts arrive in Phase 18.
