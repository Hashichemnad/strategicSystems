# deploy/

| File | Purpose |
|---|---|
| `docker-compose.yml` | Dev dependencies (`postgres`, `gotenberg`, `mailpit`) and a containerised install (profile `app`). Optional profiles: `search`, `antivirus`, `ai`. |
| `Dockerfile` | Multi-stage build of the single deployable (SPA + host), non-root runtime, built-in healthcheck. |
| `.env.example` | Settings for compose. Copy to `.env`. |
| `empty-ca.pem` | Intentionally empty default for the optional `build_ca` secret (see `BUILD_CA_FILE`). |

Install, backup and upgrade scripts arrive in Phase 18.
