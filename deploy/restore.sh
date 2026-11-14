#!/usr/bin/env bash
# Restore a backup made by backup.sh (ADR 0157) and verify it: checksums, exact row counts of every table and the audit hash chain.
#
#   ./deploy/restore.sh backups/strategy-1.0.0-20261106T020000Z            # asks for confirmation
#   ./deploy/restore.sh <folder> --yes --with-env                          # unattended; also put back deploy/.env from the backup
#
# REPLACES the current database and files of this installation. The image of the backup's version (or a newer one) must be available:
# the restore runs `migrate`, which upgrades an older backup's schema. To roll back an upgrade, restore with APP_VERSION=<old version>.
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

BACKUP=""
ASSUME_YES=false
WITH_ENV=false
while [ $# -gt 0 ]; do
  case "$1" in
    --yes | -y) ASSUME_YES=true; shift ;;
    --with-env) WITH_ENV=true; shift ;;
    -h | --help) sed -n '2,9p' "$0"; exit 0 ;;
    -*) die "Unknown option: $1 (see --help)" 2 ;;
    *) BACKUP="$1"; shift ;;
  esac
done
[ -n "$BACKUP" ] || die "Usage: ./deploy/restore.sh <backup folder> [--yes] [--with-env]" 2
BACKUP="$(cd "$BACKUP" && pwd)"
if [ ! -f "$BACKUP/manifest.json" ] || [ ! -f "$BACKUP/SHA256SUMS" ]; then die "$BACKUP is not a backup folder (manifest.json / SHA256SUMS missing)." 2; fi
require_docker

info "1/7 Checking the backup's checksums"
(cd "$BACKUP" && sha256sum --quiet -c SHA256SUMS) || die "Checksum mismatch: the backup is damaged or was changed. Nothing was restored." 7
backup_version="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$BACKUP/manifest.json" | head -n 1)"
consistent="$(sed -n 's/.*"consistent": *\([a-z]*\).*/\1/p' "$BACKUP/manifest.json" | head -n 1)"

if [ "$WITH_ENV" = true ]; then
  [ -f "$BACKUP/env" ] || die "The backup has no env file." 2
  umask 077
  cp "$BACKUP/env" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
fi
[ -f "$ENV_FILE" ] || die "deploy/.env not found. Run ./deploy/install.sh first (new machine) or pass --with-env to use the backup's settings." 2
if [ -n "${APP_VERSION:-}" ]; then env_set APP_VERSION "$APP_VERSION"; fi
APP_VERSION="$(setting APP_VERSION "$(release_version)")"
APP_IMAGE="$(app_image)"
export APP_VERSION APP_IMAGE
IMAGE_REF="$APP_IMAGE:$APP_VERSION"
ensure_local_image "$IMAGE_REF" || pull_app_image "$IMAGE_REF" \
  || die "Image $IMAGE_REF is not on this machine and could not be pulled. Load the release archive (docker load -i strategy-app-$APP_VERSION.tar.gz) or build it (docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.build.yml --profile app build migrate), then run the restore again. Nothing was changed." 4
DB="$(setting POSTGRES_DB strategy)"
OWNER="$(setting POSTGRES_USER strategy)"

say ""
say "  Backup:   $BACKUP (version $backup_version)"
say "  Restore into database '$DB' of this installation with image $IMAGE_REF."
say "  The CURRENT database and files are replaced."
if [ "$ASSUME_YES" = false ]; then
  read -r -p "  Type RESTORE to continue: " answer
  [ "$answer" = "RESTORE" ] || die "Cancelled; nothing changed." 1
fi

info "2/7 Stopping the application"
compose stop app > /dev/null 2>&1 || true
compose up -d --no-build postgres gotenberg > /dev/null
wait_for_postgres

info "3/7 Recreating database $DB and restoring the dump"
psql_owner -d postgres -c "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE);" -c "CREATE DATABASE \"$DB\" OWNER \"$OWNER\";"
# The runtime role is a cluster role: on a fresh volume init-roles.sh created it; its grants are in the dump.
compose exec -T postgres pg_restore -U "$OWNER" -d "$DB" --exit-on-error --no-password < "$BACKUP/database.dump"

info "4/7 Comparing the row count of every table with the backup"
table_counts > "$BACKUP/.restored-counts.tsv"
if diff -u "$BACKUP/table-counts.tsv" "$BACKUP/.restored-counts.tsv" > "$BACKUP/.counts.diff"; then
  say "  $(wc -l < "$BACKUP/table-counts.tsv" | tr -d ' ') tables, $(awk -F'\t' '{s += $2} END {print s + 0}' "$BACKUP/table-counts.tsv") rows: identical."
  rm -f "$BACKUP/.counts.diff"
elif [ "$consistent" = "false" ]; then
  warn "Row counts differ (online backup, counts were informative): see $BACKUP/.counts.diff"
else
  cat "$BACKUP/.counts.diff" >&2
  die "Row counts differ from the backup: the restore is incomplete. The application was NOT started." 8
fi
rm -f "$BACKUP/.restored-counts.tsv"

info "5/7 Files"
if [ -f "$BACKUP/files.tar.gz" ]; then
  compose run --rm --no-deps -T --entrypoint sh app -c 'find /var/lib/strategy/files -mindepth 1 -delete && tar xzf - -C /var/lib/strategy/files' < "$BACKUP/files.tar.gz"
else
  say "  No files archive in the backup (S3 storage): restore the bucket with the storage's own tools."
fi
if [ -f "$BACKUP/dataprotection.pfx" ]; then
  mkdir -p "$DEPLOY_DIR/secrets"
  cp "$BACKUP/dataprotection.pfx" "$DEPLOY_DIR/secrets/dataprotection.pfx"
  chmod 600 "$DEPLOY_DIR/secrets/dataprotection.pfx"
  say "  Data Protection certificate restored to deploy/secrets/dataprotection.pfx (mount it as described in deploy/README.md)."
fi

info "6/7 Migrations (owner role) and runtime-role grants"
compose run --rm --no-deps migrate

info "7/7 Verifying the audit hash chain"
# Images before 1.0 (no "Integrated Strategy System" title label; the base image has its own version label) have no --verify-audit-chain command: a rollback to them verifies on the screen instead.
if docker image inspect -f '{{json .Config.Labels}}' "$IMAGE_REF" 2> /dev/null | grep -q '"org.opencontainers.image.title":"Integrated Strategy System"'; then
  compose run --rm --no-deps app --verify-audit-chain || die "The audit chain of the restored database does not verify. The application was NOT started." 9
else
  warn "$IMAGE_REF predates 1.0 and cannot verify the chain from the command line: after the start use Audit → Verification → Verify now."
  CHAIN_NOTE="; audit chain: verify on the screen"
fi

compose up -d --no-build app > /dev/null
wait_for_app 300 || die "The application did not become healthy after the restore (docker compose … logs app)." 5
info "Restore complete and verified (checksums, row counts${CHAIN_NOTE:-, audit chain}). The application is running."
