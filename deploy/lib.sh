# shellcheck shell=bash
# Shared helpers of install.sh, backup.sh, restore.sh and upgrade.sh (sourced, not run). ADR 0157.

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # used by the scripts that source this file
ROOT_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"
ENV_FILE="$DEPLOY_DIR/.env"
COMPOSE_FILE_PATH="$DEPLOY_DIR/docker-compose.yml"
# Override with the build section, only for building from source (--build, ADR 0219).
COMPOSE_BUILD_FILE_PATH="$DEPLOY_DIR/docker-compose.build.yml"
# Release images (ADR 0219): public package on GitHub's container registry; APP_IMAGE in deploy/.env points to a customer mirror.
DEFAULT_APP_IMAGE="ghcr.io/hashichemnad/strategy-app"

say() { printf '%s\n' "$*"; }
info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mWarning:\033[0m %s\n' "$*" >&2; }
die() {
  printf '\033[1;31mError:\033[0m %s\n' "$1" >&2
  exit "${2:-1}"
}

# Version of this release folder (deploy/VERSION), used as the image tag <APP_IMAGE>:<version>.
release_version() { tr -d ' \r\n' < "$DEPLOY_DIR/VERSION"; }

# Reads KEY from deploy/.env (last occurrence; empty when absent). Never prints the value.
env_get() {
  local key="$1"
  [ -f "$ENV_FILE" ] || { printf ''; return 0; }
  awk -F= -v k="$key" '$1 == k { sub(/^[^=]*=/, ""); v = $0 } END { printf "%s", v }' "$ENV_FILE" | tr -d '\r'
}

# Sets KEY=VALUE in deploy/.env (replaces the line or appends it). Values written here never contain '|' or newlines.
env_set() {
  local key="$1" value="$2" tmp
  tmp="$(mktemp "$DEPLOY_DIR/.env.XXXXXX")"
  if grep -q "^${key}=" "$ENV_FILE"; then
    awk -F= -v k="$key" -v v="$value" '$1 == k { print k "=" v; next } { print }' "$ENV_FILE" > "$tmp"
  else
    cat "$ENV_FILE" > "$tmp"
    printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  chmod 600 "$tmp"
  mv "$tmp" "$ENV_FILE"
}

# 48 hexadecimal characters from the kernel's random source (safe in connection strings and shells).
random_secret() { od -An -tx1 -N24 /dev/urandom | tr -d ' \n'; }

# Effective setting: the caller's environment wins over deploy/.env, then the default.
setting() {
  local key="$1" default="${2:-}" value
  value="${!key:-}"
  [ -n "$value" ] || value="$(env_get "$key")"
  printf '%s' "${value:-$default}"
}

compose() { docker compose -f "$COMPOSE_FILE_PATH" --env-file "$ENV_FILE" --profile app "$@"; }
# compose with the build override (docker-compose.build.yml): building from source only.
compose_build() { docker compose -f "$COMPOSE_FILE_PATH" -f "$COMPOSE_BUILD_FILE_PATH" --env-file "$ENV_FILE" --profile app "$@"; }

# Image repository of the application (without tag): the caller's environment, deploy/.env, then the release default.
app_image() { setting APP_IMAGE "$DEFAULT_APP_IMAGE"; }

image_present() { docker image inspect "$1" > /dev/null 2>&1; }

# True when <image>:<version> is on this machine. Installations before 1.0.2 built the image as strategy-app:<version>: such a local image
# is tagged with the current name, so backups, rollbacks and restores to those versions keep working without a pull.
ensure_local_image() {
  local ref="$1" legacy
  image_present "$ref" && return 0
  legacy="strategy-app:${ref##*:}"
  if [ "$legacy" = "$ref" ] || ! image_present "$legacy"; then return 1; fi
  info "Tagging the locally built $legacy as $ref"
  docker tag "$legacy" "$ref" > /dev/null
}

# Pulls <image>:<version> for migrate and app (APP_IMAGE / APP_VERSION exported by the caller). Returns 1 with an explanation when the
# registry cannot be reached and the image is not on this machine either; a local copy of the same tag is used with a warning.
pull_app_image() {
  local ref="$1"
  info "Pulling $ref"
  if compose pull migrate app; then return 0; fi
  if ensure_local_image "$ref"; then
    warn "Could not pull $ref; using the copy already on this machine."
    return 0
  fi
  {
    say ""
    say "  Could not pull $ref (no connection to the registry, or the image does not exist there)."
    say "  Offline machine: download strategy-app-<version>.tar.gz and its .sha256 file from the GitHub release on a connected machine,"
    say "  copy both here and run the script again with --image <file>. Own registry mirror: set APP_IMAGE in deploy/.env."
    say "  Build from this source folder instead: --build."
  } >&2
  return 1
}

# Loads an offline image archive (docker save; .tar or .tar.gz) and makes sure <image>:<version> exists afterwards. <archive>.sha256 next
# to the archive (sha256sum format) is verified first: a mismatch refuses the archive before anything is loaded. An archive saved under
# another repository name with the same tag (the ghcr.io name for a mirror, or strategy-app:<version> of a source build) is tagged.
load_image_archive() {
  local archive="$1" ref="$2" version expected actual loaded name
  version="${ref##*:}"
  [ -f "$archive" ] || die "Image archive $archive not found." 4
  if [ -f "$archive.sha256" ]; then
    expected="$(awk 'NF { print tolower($1); exit }' "$archive.sha256" | tr -d '\r')"
    actual="$(sha256_of "$archive")"
    [ "$expected" = "$actual" ] \
      || die "Checksum mismatch: $archive has SHA-256 $actual but $archive.sha256 expects $expected. Download the archive again; nothing was loaded." 4
    info "Checksum verified ($archive.sha256)"
  else
    warn "No $archive.sha256 next to the archive: its checksum was not verified."
  fi
  info "Loading the image from $archive"
  loaded="$(docker load -i "$archive")" || die "docker load failed for $archive." 4
  say "$loaded"
  if ! image_present "$ref"; then
    while IFS= read -r name; do
      case "$name" in
        *strategy-app:"$version")
          info "Tagging $name as $ref"
          docker tag "$name" "$ref" || die "docker tag $name $ref failed." 4
          break
          ;;
      esac
    done < <(printf '%s\n' "$loaded" | sed -n 's/^Loaded image: //p')
  fi
  image_present "$ref" || die "The archive does not contain $ref (nor another strategy-app:$version to tag)." 4
}

# Builds <APP_IMAGE>:<APP_VERSION> from this source folder with the build override (CA bundle, revision and creation labels).
build_app_image() {
  local revision
  prepare_build_ca
  revision="$(source_revision)"
  GIT_REVISION="$revision" BUILD_CREATED="$(date -u +%Y-%m-%dT%H:%M:%SZ)" compose_build build migrate
}

require_docker() {
  command -v docker > /dev/null 2>&1 || die "Docker is not installed. Install Docker Engine 24+ (https://docs.docker.com/engine/install/) and run this script again." 10
  docker info > /dev/null 2>&1 || die "Docker is installed but the daemon is not reachable. Start Docker (sudo systemctl start docker) or run this script as a user in the 'docker' group." 10
  docker compose version > /dev/null 2>&1 || die "Docker Compose v2 is missing ('docker compose'). Install the docker-compose-plugin package." 10
}

# True when something already listens on 127.0.0.1:<port> or the port is bound on any address.
port_in_use() {
  local port="$1"
  if command -v ss > /dev/null 2>&1; then
    ss -Hltn "sport = :$port" 2> /dev/null | grep -q . && return 0
  fi
  (exec 3<> "/dev/tcp/127.0.0.1/$port") 2> /dev/null && return 0
  return 1
}

# Port published by a running container of this installation (so a re-run does not report its own ports as busy).
port_owned_by_install() {
  local port="$1"
  compose ps --format '{{.Publishers}}' 2> /dev/null | grep -Eq ":${port}(\]|,| |$)" && return 0
  return 1
}

# Waits until the app container reports healthy (Dockerfile HEALTHCHECK), up to $1 seconds.
wait_for_app() {
  local timeout="${1:-300}" waited=0 id status
  while [ "$waited" -lt "$timeout" ]; do
    id="$(compose ps -q app 2> /dev/null || true)"
    if [ -n "$id" ]; then
      status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$id" 2> /dev/null || true)"
      case "$status" in
        healthy) return 0 ;;
        exited | dead) return 1 ;;
      esac
    fi
    sleep 3
    waited=$((waited + 3))
  done
  return 1
}

wait_for_postgres() {
  local waited=0 user db
  user="$(setting POSTGRES_USER strategy)"
  db="$(setting POSTGRES_DB strategy)"
  until compose exec -T postgres pg_isready -U "$user" -d "$db" > /dev/null 2>&1; do
    sleep 2
    waited=$((waited + 2))
    [ "$waited" -lt 120 ] || die "PostgreSQL did not become ready within 120 s (docker compose logs postgres)." 20
  done
}

# psql as the owner inside the postgres container; extra arguments are passed through.
psql_owner() {
  compose exec -T postgres psql -v ON_ERROR_STOP=1 -X -q -U "$(setting POSTGRES_USER strategy)" "$@"
}

# Exact row count of every table in the application schemas, as "schema.table<TAB>count" lines (sorted). Used by backup and restore.
# Quartz runtime tables change on every scheduler heartbeat even with the application stopped for a moment, so they are excluded.
table_counts() {
  local db
  db="$(setting POSTGRES_DB strategy)"
  psql_owner -d "$db" -At -F $'\t' <<'SQL'
SELECT n.nspname || '.' || c.relname,
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text::bigint
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
  AND NOT (n.nspname = 'scheduling' AND c.relname LIKE 'qrtz_%')
ORDER BY 1;
SQL
}

# Behind a TLS-inspecting proxy the build needs the organisation's CA bundle (BUILD_CA_FILE). Compose only reads build secrets inside
# the project folder without extra privileges, so a bundle elsewhere is copied to deploy/build-ca.local.pem (ignored by version control).
prepare_build_ca() {
  local ca
  ca="$(setting BUILD_CA_FILE)"
  [ -n "$ca" ] || return 0
  case "$ca" in
    ./* | "$DEPLOY_DIR"/*) return 0 ;;
  esac
  [ -f "$ca" ] || die "BUILD_CA_FILE=$ca not found." 2
  cp "$ca" "$DEPLOY_DIR/build-ca.local.pem"
  env_set BUILD_CA_FILE ./build-ca.local.pem
  export BUILD_CA_FILE=./build-ca.local.pem
}

sha256_of() {
  if command -v sha256sum > /dev/null 2>&1; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

# Commit of this release folder for the image label, or empty (ADR 0163): a ZIP release has no .git, git may be missing, and a failing
# rev-parse (broken .git, dubious ownership) only means "no revision" (stderr discarded, exit code tolerated under set -e). Without .git
# the program is not run at all, so a release unpacked inside another repository never takes that repository's commit.
source_revision() {
  [ -e "$ROOT_DIR/.git" ] || return 0
  command -v git > /dev/null 2>&1 || return 0
  git -C "$ROOT_DIR" rev-parse --short HEAD 2> /dev/null || true
}
