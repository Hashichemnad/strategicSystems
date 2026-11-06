#!/usr/bin/env bash
# Load test of the NFRs (docs/performance.md, ADR 0158): seeds a database with the Phase 17 volume, starts the Release host on it, runs the
# k6 scripts in a container (grafana/k6, AGPL-3.0, a test tool only — never part of the product) and leaves the results in tests/perf/results/.
#
#   PERF_ADMIN_CONNECTION  admin connection string of a PostgreSQL 16 server (default: local postgres/postgres)
#   PERF_DB                database name (dropped and re-created; default strategy_perf)
#   PERF_PORT              host port (default 5197) · MAILPIT_SMTP / MAILPIT_HTTP ports (default 1325 / 8325)
#   LOAD_VUS               virtual users of the concurrency phase (default 100)
#   REGISTRY_PREFIX        image mirror, e.g. mirror.gcr.io/
#   KEEP=1                 keep the database, Mailpit and the host running afterwards
set -euo pipefail
cd "$(dirname "$0")/../.."

PERF_DB="${PERF_DB:-strategy_perf}"
PERF_PORT="${PERF_PORT:-5197}"
MAILPIT_SMTP="${MAILPIT_SMTP:-1325}"
MAILPIT_HTTP="${MAILPIT_HTTP:-8325}"
ADMIN="${PERF_ADMIN_CONNECTION:-Host=localhost;Port=5432;Username=postgres;Password=postgres}"
DB_CONNECTION="${ADMIN};Database=${PERF_DB}"
OUT="tests/perf/results"
mkdir -p "$OUT"

psql_admin() { # runs one statement on the server's postgres database (connection string → psql keywords)
  local host port user password
  host=$(sed -E 's/.*Host=([^;]+).*/\1/' <<<"$ADMIN"); port=$(sed -E 's/.*Port=([^;]+).*/\1/' <<<"$ADMIN")
  user=$(sed -E 's/.*Username=([^;]+).*/\1/' <<<"$ADMIN"); password=$(sed -E 's/.*Password=([^;]+).*/\1/' <<<"$ADMIN")
  PGPASSWORD="$password" psql -h "$host" -p "$port" -U "$user" -d postgres -v ON_ERROR_STOP=1 -qc "$1"
}

echo "== Database ${PERF_DB}"
psql_admin "DROP DATABASE IF EXISTS \"${PERF_DB}\" WITH (FORCE)"
psql_admin "CREATE DATABASE \"${PERF_DB}\""

echo "== Mailpit"
docker rm -f strategy-perf-mailpit >/dev/null 2>&1 || true
docker run -d --name strategy-perf-mailpit -p "${MAILPIT_SMTP}:1025" -p "${MAILPIT_HTTP}:8025" "${REGISTRY_PREFIX:-}axllent/mailpit:v1.29" >/dev/null

echo "== Build (Release) and seed"
dotnet build tests/Strategy.IntegrationTests -c Release -v quiet --nologo
STRATEGY_TEST_POSTGRES="$ADMIN" STRATEGY_PERF_DATABASE="$DB_CONNECTION" STRATEGY_PERF_OUTPUT="$PWD/$OUT/seed.json" STRATEGY_PERF_SMTP_PORT="$MAILPIT_SMTP" \
  dotnet test --project tests/Strategy.IntegrationTests -c Release --no-build -- --filter-class "*PerformanceVolumeSeed"

echo "== Host on :${PERF_PORT}"
pushd src/Host/Strategy.Host/bin/Release/net10.0 >/dev/null
# Development only for the configuration of modules / demo AI; the rate limits are raised because every virtual user comes from one IP.
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__Strategy="${DB_CONNECTION};Maximum Pool Size=200" Seed__Demo=false \
  RateLimiting__PermitLimit=1000000 RateLimiting__AuthPermitLimit=100000 Serilog__MinimumLevel__Default=Warning Spa__RootPath=/nonexistent \
  Files__LocalPath="${TMPDIR:-/tmp}/strategy-perf-files" nohup dotnet Strategy.Host.dll --urls "http://localhost:${PERF_PORT}" >"$OLDPWD/$OUT/host.log" 2>&1 &
HOST_PID=$!
popd >/dev/null
cleanup() {
  if [ "${KEEP:-0}" != "1" ]; then
    kill "$HOST_PID" 2>/dev/null || true
    docker rm -f strategy-perf-mailpit >/dev/null 2>&1 || true
    psql_admin "DROP DATABASE IF EXISTS \"${PERF_DB}\" WITH (FORCE)" || true
  fi
}
trap cleanup EXIT
until curl -sf "http://localhost:${PERF_PORT}/health/ready" >/dev/null; do sleep 2; done

k6() {
  docker run --rm --network host -u "$(id -u):$(id -g)" -v "$PWD/tests/perf:/scripts" -e SEED_FILE=/scripts/results/seed.json -e BASE_URL="http://localhost:${PERF_PORT}" \
    -e MAILPIT_URL="http://localhost:${MAILPIT_HTTP}" -e LOAD_VUS="${LOAD_VUS:-100}" "${REGISTRY_PREFIX:-}grafana/k6:latest" "$@"
}

echo "== Notification latency"
k6 run --summary-export /scripts/results/notify-summary.json /scripts/notify.js | tee "$OUT/notify.txt"
echo "== NFR load test"
k6 run --summary-export /scripts/results/nfr-summary.json /scripts/nfr.js | tee "$OUT/nfr.txt"
echo "Results in $OUT/"
