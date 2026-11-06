# Dependency, container and secret scan results (release 1.0)

Run on 2026-11-06 (Phase 18). Gate: **no High / Critical finding with a fix available**. Tools run as containers or from the repository;
none is shipped with the product (design note `docs/design/release.md` §8).

| Scan | Command | Result |
|---|---|---|
| NuGet (direct + transitive) | `tools/check-vulnerable.sh` | **No High / Critical** |
| npm (production + dev, lock file) | `cd web && npm audit --audit-level=high` | **No High / Critical**; 8 moderate (development tooling only, not in the built SPA) |
| Repository (NuGet `deps.json`, `Directory.Packages.props`, `package-lock.json`) + secrets | `trivy fs --scanners vuln,secret --severity HIGH,CRITICAL .` | **0 vulnerabilities, 0 secrets** (159 dependency files) |
| Container image (OS packages, .NET runtime, app `deps.json`) + secrets | `trivy image --scanners vuln,secret --severity HIGH,CRITICAL strategy-app:<version>` | Before the fix: **2 HIGH** — `apt` / `libapt-pkg6.0t64` 2.8.3, CVE-2026-109518 (fixed in 2.8.4ubuntu0.1); .NET runtime 10.0.14 and application `deps.json`: 0; secrets: 0 |

## Fix

`deploy/Dockerfile` runtime stage now runs `apt-get upgrade -y --no-install-recommends` before installing LDAP / Tesseract, so every OS
package fixed after the `mcr.microsoft.com/dotnet/aspnet:10.0` base image was published is included at build time. Rebuilding the image
picks up the fixed `apt` 2.8.4ubuntu0.1. The image was scanned before the fix (the scan host had no disk space left for a second 1.3 GB
build); CI's `docker` job rebuilds the image on every pull request, and the release checklist re-runs the image scan on the tagged build.

## How to reproduce

```bash
# Behind a TLS-inspecting proxy, mount its CA bundle and set SSL_CERT_FILE; in restricted networks use the mirror.gcr.io DB repositories.
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v trivy-cache:/root/.cache/trivy \
  mirror.gcr.io/aquasec/trivy:latest image --db-repository mirror.gcr.io/aquasec/trivy-db \
  --scanners vuln,secret --severity HIGH,CRITICAL strategy-app:1.0.0
```

```bash
docker run --rm -v trivy-cache:/root/.cache/trivy -v "$PWD":/src:ro mirror.gcr.io/aquasec/trivy:latest \
  fs --db-repository mirror.gcr.io/aquasec/trivy-db --scanners vuln,secret --severity HIGH,CRITICAL /src
```

```bash
tools/check-vulnerable.sh && (cd web && npm audit --audit-level=high)
```

## Secrets never in images or logs

- The image contains no `.env`, connection strings or keys: configuration comes from the environment at run time; the Data Protection key
  ring lives in the database (ADR 0012). Trivy's secret scanner found nothing in the image or the repository.
- Logs: request logging records method, path (without query string), status and duration; no log template includes a password, key,
  token or connection string; EF Core sensitive-data logging is off; provider keys and SMTP passwords are never returned by the API and
  never written to the audit trail (IT `Provider_keys_are_never_returned_and_never_written_to_the_audit_trail`,
  `Smtp_password_is_stored_encrypted_and_never_returned`). The Health page returns parameters only (no exception messages).
