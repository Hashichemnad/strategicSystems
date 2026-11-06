# shellcheck shell=bash
# Shared helpers of install.sh, backup.sh, restore.sh and upgrade.sh (sourced, not run). ADR 0157.

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # used by the scripts that source this file
ROOT_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"
ENV_FILE="$DEPLOY_DIR/.env"
COMPOSE_FILE_PATH="$DEPLOY_DIR/docker-compose.yml"

say() { printf '%s\n' "$*"; }
info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mWarning:\033[0m %s\n' "$*" >&2; }
die() {
  printf '\033[1;31mError:\033[0m %s\n' "$1" >&2
  exit "${2:-1}"
}

# Version of this release folder (deploy/VERSION), used as the image tag strategy-app:<version>.
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
