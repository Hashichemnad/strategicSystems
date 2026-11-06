#!/usr/bin/env bash
# In-place upgrade of a Docker installation to the version of this release folder (ADR 0157):
# pre-upgrade backup → new image → migrations as the owner + runtime-role grants → start → health check.
#
#   ./deploy/upgrade.sh                              # build the new image from this folder
#   ./deploy/upgrade.sh --image strategy-app-1.0.1.tar   # offline: load the new image
#   ./deploy/upgrade.sh --no-backup                  # only when a backup was just taken
#
# Rollback (printed again on failure): APP_VERSION=<previous version> ./deploy/restore.sh <pre-upgrade backup> --yes
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

IMAGE_ARCHIVE=""
BACKUP=true
while [ $# -gt 0 ]; do
  case "$1" in
    --image) IMAGE_ARCHIVE="${2:?--image needs the path of a saved image}"; shift 2 ;;
    --no-backup) BACKUP=false; shift ;;
    -h | --help) sed -n '2,10p' "$0"; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" 2 ;;
  esac
done

require_docker
[ -f "$ENV_FILE" ] || die "deploy/.env not found: no installation here. Use ./deploy/install.sh for a new installation." 2
OLD_VERSION="$(setting APP_VERSION dev)"
NEW_VERSION="${NEW_VERSION:-$(release_version)}"
info "Upgrade strategy-app $OLD_VERSION → $NEW_VERSION"

BACKUP_DIR=""
if [ "$BACKUP" = true ]; then
  info "Pre-upgrade backup"
  result="$(mktemp)"
  BACKUP_RESULT_FILE="$result" "$DEPLOY_DIR/backup.sh" || { rm -f "$result"; die "The pre-upgrade backup failed; upgrade stopped, nothing changed." 6; }
  BACKUP_DIR="$(cat "$result")"
  rm -f "$result"
fi

rollback_help() {
  say ""
  say "  The upgrade did not complete. The previous image strategy-app:$OLD_VERSION is still on this machine."
  if [ -n "$BACKUP_DIR" ]; then
    say "  Roll back (restores the database and files taken just before the upgrade):"
    say "      APP_VERSION=$OLD_VERSION ./deploy/restore.sh $BACKUP_DIR --yes"
  else
    say "  Roll back: restore your latest backup with APP_VERSION=$OLD_VERSION ./deploy/restore.sh <backup> --yes"
  fi
  say "  Logs: docker compose -f deploy/docker-compose.yml --profile app logs migrate app"
}

export APP_VERSION="$NEW_VERSION"
if [ -n "$IMAGE_ARCHIVE" ]; then
  info "Loading the new image"
  docker load -i "$IMAGE_ARCHIVE"
  docker image inspect "strategy-app:$NEW_VERSION" > /dev/null 2>&1 || die "The archive does not contain strategy-app:$NEW_VERSION." 4
else
  info "Building strategy-app:$NEW_VERSION"
  prepare_build_ca
  revision="$( (git -C "$ROOT_DIR" rev-parse --short HEAD 2> /dev/null) || true)"
  GIT_REVISION="$revision" BUILD_CREATED="$(date -u +%Y-%m-%dT%H:%M:%SZ)" compose build migrate || { rollback_help; die "Image build failed; the running version is unchanged." 4; }
fi

info "Stopping the application"
compose stop app > /dev/null 2>&1 || true
info "Applying migrations as the database owner and granting the runtime role"
if ! compose run --rm migrate; then
  rollback_help
  die "Migrations failed." 7
fi
env_set APP_VERSION "$NEW_VERSION"

info "Starting strategy-app:$NEW_VERSION"
compose up -d --no-build
if ! wait_for_app 420; then
  compose logs --tail 60 app >&2 || true
  rollback_help
  die "The new version did not become healthy." 5
fi
info "Upgrade to $NEW_VERSION complete. Keep the pre-upgrade backup until you have checked the application${BACKUP_DIR:+: $BACKUP_DIR}."
say "  Read the release notes (About in the user menu) — e.g. reminders and digests that start sending after this upgrade."
