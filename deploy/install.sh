#!/usr/bin/env bash
# Integrated Strategy System — one-command install on Linux (Docker Engine + Compose v2). ADR 0157, docs/guides/en/installation.md.
#
#   ./deploy/install.sh                       # pull the release image (ghcr.io, or APP_IMAGE), start, print the setup address
#   ./deploy/install.sh --image strategy-app-1.0.1.tar.gz   # offline: load the release archive (checks <file>.sha256 when present)
#   ./deploy/install.sh --build               # build the image from this source folder instead (docker-compose.build.yml)
#   ./deploy/install.sh --no-pull             # the image <APP_IMAGE>:<version> is already on this machine (--no-build: old name)
#   APP_PORT=8090 ./deploy/install.sh         # another port (also POSTGRES_PORT, GOTENBERG_PORT; saved in deploy/.env)
#
# Safe to run again: an existing deploy/.env (passwords) is kept, running containers are updated in place.
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

IMAGE_ARCHIVE=""
MODE=pull
set_mode() { [ "$MODE" = pull ] || [ "$MODE" = "$1" ] || die "Choose one of --image, --build and --no-pull." 2; MODE="$1"; }
while [ $# -gt 0 ]; do
  case "$1" in
    --image) IMAGE_ARCHIVE="${2:?--image needs the path of the release image archive (.tar or .tar.gz)}"; set_mode archive; shift 2 ;;
    --build) set_mode build; shift ;;
    --no-pull) set_mode local; shift ;;
    --no-build) warn "--no-build is deprecated: use --no-pull (the image is pulled by default since 1.0.2)."; set_mode local; shift ;;
    -h | --help) sed -n '2,11p' "$0"; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" 2 ;;
  esac
done

VERSION="$(release_version)"
info "Integrated Strategy System $VERSION — installation"
require_docker

# 1. Settings file with generated passwords (kept when it already exists).
if [ ! -f "$ENV_FILE" ]; then
  info "Creating deploy/.env with generated passwords"
  umask 077
  cp "$DEPLOY_DIR/.env.example" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  env_set POSTGRES_PASSWORD "$(random_secret)"
  env_set STRATEGY_APP_PASSWORD "$(random_secret)"
  env_set MEILI_MASTER_KEY "$(random_secret)"
  env_set APP_VERSION "$VERSION"
else
  info "Keeping the existing deploy/.env"
  for key in POSTGRES_PASSWORD STRATEGY_APP_PASSWORD; do
    case "$(env_get "$key")" in
      "" | change-me | change-me-too | strategy | strategy_app) die "deploy/.env still has the example value of $key. Set a strong password (or delete deploy/.env to generate new ones on a NEW installation)." 2 ;;
    esac
  done
fi

# 2. Ports: a clear message instead of a failed container start.
busy=0
for pair in "APP_PORT:8080:the application" "POSTGRES_PORT:5432:PostgreSQL (loopback only)" "GOTENBERG_PORT:3000:Gotenberg PDF service (loopback only)"; do
  key="${pair%%:*}"
  rest="${pair#*:}"
  default="${rest%%:*}"
  what="${rest#*:}"
  port="$(setting "$key" "$default")"
  if port_in_use "$port" && ! port_owned_by_install "$port"; then
    busy=1
    say ""
    say "  Port $port ($key, $what) is already in use on this machine."
    say "  Choose a free port and run the installer again, for example:"
    say "      $key=$((port + 10)) ./deploy/install.sh"
    say "  or set $key=<port> in deploy/.env. (Find the program using it: sudo ss -ltnp 'sport = :$port')"
  fi
done
[ "$busy" -eq 0 ] || { say ""; die "Installation stopped: port(s) in use. Nothing was started." 3; }

# Ports and options given on the command line are saved, so later commands (backup, upgrade) use the same values.
for key in APP_PORT POSTGRES_PORT GOTENBERG_PORT APP_BIND REGISTRY_PREFIX BUILD_CA_FILE APP_IMAGE; do
  if [ -n "${!key:-}" ]; then env_set "$key" "${!key}"; fi
done
[ -n "$(env_get APP_VERSION)" ] || env_set APP_VERSION "$VERSION"
[ -n "$(env_get APP_IMAGE)" ] || env_set APP_IMAGE "$DEFAULT_APP_IMAGE"
APP_VERSION="$(env_get APP_VERSION)"
APP_IMAGE="$(env_get APP_IMAGE)"
export APP_VERSION APP_IMAGE
IMAGE_REF="$APP_IMAGE:$APP_VERSION"

# 3. Image: pull the release image (default), load the offline archive, build it from this folder, or use the local copy.
case "$MODE" in
  pull) pull_app_image "$IMAGE_REF" || die "Installation stopped: image $IMAGE_REF not available. Nothing was started." 4 ;;
  archive) load_image_archive "$IMAGE_ARCHIVE" "$IMAGE_REF" ;;
  build)
    info "Building $IMAGE_REF from this folder (first build takes several minutes)"
    build_app_image
    ;;
  local) ensure_local_image "$IMAGE_REF" || die "--no-pull: image $IMAGE_REF is not on this machine. Run without --no-pull to pull it, or use --image <archive>." 4 ;;
esac

# 4. Start: postgres, gotenberg, migrate (owner role, grants the runtime role), app.
info "Starting the services"
compose up -d --no-build
info "Waiting for the application to become ready"
if ! wait_for_app 420; then
  compose logs --tail 60 migrate app >&2 || true
  die "The application did not become healthy. See the logs above (docker compose -f deploy/docker-compose.yml --profile app logs app)." 5
fi

PORT="$(setting APP_PORT 8080)"
HOST="$(hostname -f 2> /dev/null || hostname)"
say ""
info "Installed. The application is running at http://$HOST:$PORT (put an HTTPS reverse proxy in front of it, deploy/nginx/strategy.conf)."

# 5. First administrator: the setup wizard with the one-time setup token from the application log.
# The log is JSON (Serilog): the token is the "SetupToken" property of the "FIRST-RUN SETUP" warning (ADR 0150).
token="$(compose logs --no-color app 2> /dev/null | grep -o '"SetupToken":"[^"]*"' | tail -n 1 | sed 's/^"SetupToken":"//; s/"$//' || true)"
if [ -n "$token" ]; then
  say ""
  say "  Open http://$HOST:$PORT/setup and enter the one-time setup token:"
  say "      $token"
  say "  (Print a new one later: docker compose -f deploy/docker-compose.yml --profile app run --rm app --setup-token)"
else
  say ""
  say "  No setup token was printed (an administrator already exists, or this build has no setup wizard)."
  say "  To create the first administrator from the command line:"
  say "      read -rsp 'Administrator password: ' P && echo && printf '%s' \"\$P\" | \\"
  say "        docker compose -f deploy/docker-compose.yml --profile app run --rm -T app --create-admin --email admin@example.org --password-file /dev/stdin"
fi
say ""
say "  Back up regularly: ./deploy/backup.sh   ·   Upgrade: ./deploy/upgrade.sh   ·   Guide: docs/guides/en/installation.md"
