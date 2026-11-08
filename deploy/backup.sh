#!/usr/bin/env bash
# Backup of a Docker installation (ADR 0157): database (incl. the Data Protection key ring), files volume, deploy/.env and the optional
# Data Protection certificate, with a manifest (version, checksums, exact row counts) that restore.sh verifies.
#
#   ./deploy/backup.sh                         # → backups/strategy-<version>-<UTC timestamp>/
#   ./deploy/backup.sh --output /srv/backups   # another parent folder
#   ./deploy/backup.sh --online                # do not stop the application (row counts are then informative only)
#
# By default the application is stopped for the duration of the dump (usually seconds to a few minutes) so that the database, the files
# and the row counts are one consistent point in time; it is started again afterwards, also when the backup fails.
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

OUTPUT_PARENT="$ROOT_DIR/backups"
ONLINE=false
LEAVE_STOPPED=false
CERTIFICATE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --output) OUTPUT_PARENT="${2:?--output needs a folder}"; shift 2 ;;
    --online) ONLINE=true; shift ;;
    --leave-stopped) LEAVE_STOPPED=true; shift ;;   # used by upgrade.sh: the application stays stopped after a successful backup
    --certificate) CERTIFICATE="${2:?--certificate needs the path of the .pfx}"; shift 2 ;;
    -h | --help) sed -n '2,10p' "$0"; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" 2 ;;
  esac
done

require_docker
[ -f "$ENV_FILE" ] || die "deploy/.env not found: this folder has no installation to back up." 2
VERSION="$(setting APP_VERSION "$(release_version)")"
DB="$(setting POSTGRES_DB strategy)"
OWNER="$(setting POSTGRES_USER strategy)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="$OUTPUT_PARENT/strategy-$VERSION-$STAMP"
# Two backups in the same second (e.g. an upgrade right after a manual backup) never share a folder.
N=1
while [ -e "$OUT" ]; do N=$((N + 1)); OUT="$OUTPUT_PARENT/strategy-$VERSION-$STAMP-$N"; done
umask 077
mkdir -p "$OUT"
chmod 700 "$OUT"
info "Backup to $OUT"

compose up -d postgres > /dev/null 2>&1 || true
wait_for_postgres

app_was_running=false
if [ -n "$(compose ps -q --status running app 2> /dev/null || true)" ]; then app_was_running=true; fi
restart_app() {
  if [ "$app_was_running" = true ] && [ "$ONLINE" = false ]; then
    info "Starting the application again"
    compose up -d --no-build --no-deps app > /dev/null 2>&1 || warn "Could not start the application; run: docker compose -f deploy/docker-compose.yml --profile app up -d"
  fi
}
trap restart_app EXIT
if [ "$app_was_running" = true ] && [ "$ONLINE" = false ]; then
  info "Stopping the application for a consistent backup"
  compose stop app > /dev/null
fi

info "Database $DB (pg_dump custom format, includes core.data_protection_keys)"
compose exec -T postgres pg_dump -U "$OWNER" -d "$DB" -Fc --no-password > "$OUT/database.dump"
[ -s "$OUT/database.dump" ] || die "pg_dump produced an empty file." 6

info "Row counts"
table_counts > "$OUT/table-counts.tsv"

FILES_PROVIDER="$(setting FILES_PROVIDER local)"
if [ "$FILES_PROVIDER" = "local" ]; then
  info "Files volume"
  compose run --rm --no-deps -T --entrypoint tar app czf - -C /var/lib/strategy/files . > "$OUT/files.tar.gz"
else
  warn "FILES_PROVIDER=$FILES_PROVIDER: documents are in the S3 bucket; back it up with the storage's own tools (versioning / replication)."
fi

info "Settings (deploy/.env — contains passwords, keep the backup folder protected)"
cp "$ENV_FILE" "$OUT/env"
if [ -z "$CERTIFICATE" ] && [ -f "$DEPLOY_DIR/secrets/dataprotection.pfx" ]; then CERTIFICATE="$DEPLOY_DIR/secrets/dataprotection.pfx"; fi
if [ -n "$CERTIFICATE" ]; then
  [ -f "$CERTIFICATE" ] || die "Certificate $CERTIFICATE not found." 2
  info "Data Protection certificate"
  cp "$CERTIFICATE" "$OUT/dataprotection.pfx"
fi

[ "$LEAVE_STOPPED" = true ] || restart_app
trap - EXIT

# Manifest: what restore.sh checks.
(cd "$OUT" && for f in database.dump files.tar.gz env dataprotection.pfx table-counts.tsv; do [ -f "$f" ] && sha256sum "$f"; done) > "$OUT/SHA256SUMS"
{
  printf '{\n  "format": 1,\n  "product": "strategy",\n  "version": "%s",\n  "createdUtc": "%s",\n' "$VERSION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf '  "database": "%s",\n  "filesProvider": "%s",\n  "consistent": %s,\n  "parts": {\n' "$DB" "$FILES_PROVIDER" "$([ "$ONLINE" = true ] && echo false || echo true)"
  first=true
  while read -r sum name; do
    [ "$first" = true ] || printf ',\n'
    first=false
    printf '    "%s": { "sha256": "%s", "bytes": %s }' "$name" "$sum" "$(wc -c < "$OUT/$name" | tr -d ' ')"
  done < "$OUT/SHA256SUMS"
  printf '\n  },\n  "tables": %s,\n  "rows": %s\n}\n' "$(wc -l < "$OUT/table-counts.tsv" | tr -d ' ')" "$(awk -F'\t' '{s += $2} END {print s + 0}' "$OUT/table-counts.tsv")"
} > "$OUT/manifest.json"

# upgrade.sh reads the folder of the backup it asked for.
if [ -n "${BACKUP_RESULT_FILE:-}" ]; then printf '%s\n' "$OUT" > "$BACKUP_RESULT_FILE"; fi
info "Backup complete: $OUT ($(du -sh "$OUT" | cut -f1))"
say "  Copy it to another machine or storage. Restore: ./deploy/restore.sh $OUT"
