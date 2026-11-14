#!/usr/bin/env bash
# In-place upgrade of a Docker installation to the version of this release folder (ADR 0157):
# new image (pulled while the current version runs) → pre-upgrade backup → migrations as the owner + runtime-role grants → start → health check.
#
#   ./deploy/upgrade.sh                              # pull the new release image (ghcr.io, or APP_IMAGE in deploy/.env)
#   ./deploy/upgrade.sh --image strategy-app-1.0.2.tar.gz   # offline: load the release archive (checks <file>.sha256 when present)
#   ./deploy/upgrade.sh --build                      # build the new image from this source folder (docker-compose.build.yml)
#   ./deploy/upgrade.sh --no-pull                    # the new image <APP_IMAGE>:<version> is already on this machine (--no-build: old name)
#   ./deploy/upgrade.sh --no-backup                  # only when a backup was just taken
#
# Rollback (printed again on failure): APP_VERSION=<previous version> ./deploy/restore.sh <pre-upgrade backup> --yes
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

IMAGE_ARCHIVE=""
BACKUP=true
MODE=pull
set_mode() { [ "$MODE" = pull ] || [ "$MODE" = "$1" ] || die "Choose one of --image, --build and --no-pull." 2; MODE="$1"; }
while [ $# -gt 0 ]; do
  case "$1" in
    --image) IMAGE_ARCHIVE="${2:?--image needs the path of the release image archive (.tar or .tar.gz)}"; set_mode archive; shift 2 ;;
    --no-backup) BACKUP=false; shift ;;
    --build) set_mode build; shift ;;
    --no-pull) set_mode local; shift ;;
    --no-build) warn "--no-build is deprecated: use --no-pull (the image is pulled by default since 1.0.2)."; set_mode local; shift ;;
    -h | --help) sed -n '2,12p' "$0"; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" 2 ;;
  esac
done

require_docker
[ -f "$ENV_FILE" ] || die "deploy/.env not found: no installation here. Use ./deploy/install.sh for a new installation." 2
OLD_VERSION="$(setting APP_VERSION dev)"
NEW_VERSION="${NEW_VERSION:-$(release_version)}"
# Installations before 1.0.2 have no APP_IMAGE in deploy/.env (their image was built as strategy-app:<version>): the release default.
[ -n "$(env_get APP_IMAGE)" ] || env_set APP_IMAGE "$(app_image)"
APP_IMAGE="$(app_image)"
export APP_IMAGE
OLD_REF="$APP_IMAGE:$OLD_VERSION"
NEW_REF="$APP_IMAGE:$NEW_VERSION"
info "Upgrade strategy-app $OLD_VERSION → $NEW_VERSION"
BACKUP_DIR=""
# The running version's image under the current name (tags a pre-1.0.2 strategy-app:<version>): the pre-upgrade backup and a rollback use it.
ensure_local_image "$OLD_REF" || warn "The current image $OLD_REF is not on this machine; the pre-upgrade backup and a rollback will try to pull it."

rollback_help() {
  say ""
  say "  The upgrade did not complete. The previous image $OLD_REF is still on this machine."
  if [ -n "$BACKUP_DIR" ]; then
    say "  Roll back (restores the database and files taken just before the upgrade):"
    say "      APP_VERSION=$OLD_VERSION ./deploy/restore.sh $BACKUP_DIR --yes"
  else
    say "  Roll back: restore your latest backup with APP_VERSION=$OLD_VERSION ./deploy/restore.sh <backup> --yes"
  fi
  say "  Logs: docker compose -f deploy/docker-compose.yml --profile app logs migrate app"
}

# 1. New image first, while the current version keeps running (a failed pull, load or build changes nothing).
case "$MODE" in
  pull) APP_VERSION="$NEW_VERSION" pull_app_image "$NEW_REF" || die "Upgrade stopped: image $NEW_REF not available; the running version is unchanged." 4 ;;
  archive) load_image_archive "$IMAGE_ARCHIVE" "$NEW_REF" ;;
  build)
    info "Building $NEW_REF (the current version keeps running)"
    APP_VERSION="$NEW_VERSION" build_app_image || die "Image build failed; the running version is unchanged." 4
    ;;
  local) ensure_local_image "$NEW_REF" || die "--no-pull: image $NEW_REF is not on this machine; nothing changed." 4 ;;
esac

# 2. Pre-upgrade backup with the current version; the application stays stopped afterwards.
if [ "$BACKUP" = true ]; then
  info "Pre-upgrade backup"
  result="$(mktemp)"
  APP_VERSION="$OLD_VERSION" BACKUP_RESULT_FILE="$result" "$DEPLOY_DIR/backup.sh" --leave-stopped || {
    rm -f "$result"
    APP_VERSION="$OLD_VERSION" compose up -d --no-build > /dev/null 2>&1 || true
    die "The pre-upgrade backup failed; upgrade stopped, the current version was started again." 6
  }
  BACKUP_DIR="$(cat "$result")"
  rm -f "$result"
fi

# 3. Migrations as the owner (and runtime-role grants), then the new version.
export APP_VERSION="$NEW_VERSION"
info "Stopping the application"
compose stop app > /dev/null 2>&1 || true
info "Applying migrations as the database owner and granting the runtime role"
if ! compose run --rm migrate; then
  rollback_help
  die "Migrations failed." 7
fi
env_set APP_VERSION "$NEW_VERSION"

info "Starting $NEW_REF"
compose up -d --no-build
if ! wait_for_app 420; then
  compose logs --tail 60 app >&2 || true
  rollback_help
  die "The new version did not become healthy." 5
fi
info "Upgrade to $NEW_VERSION complete. Keep the pre-upgrade backup until you have checked the application${BACKUP_DIR:+: $BACKUP_DIR}."
say "  Read the release notes (About in the user menu) — e.g. reminders and digests that start sending after this upgrade."
