#!/usr/bin/env bash
# Tests of deploy/install.sh, backup.sh, restore.sh and upgrade.sh against a fake docker (ADR 0163): a temporary release folder WITHOUT
# .git (non-ASCII path), the fake docker (tests/deploy/fake-docker) first on PATH; asserts exit codes, docker calls and written files.
#   tests/deploy/test-deploy-scripts.sh            # needs bash, python3, sha256sum; git optional (broken-.git case)
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# The release under test (deploy/VERSION) and two later patch versions for the upgrade cases: no test edit on a version bump.
CURRENT="$(tr -d '[:space:]' < "$REPO/deploy/VERSION")"
NEXT="${CURRENT%.*}.$(( ${CURRENT##*.} + 1 ))"
AFTER="${CURRENT%.*}.$(( ${CURRENT##*.} + 2 ))"
CURRENT_RE="${CURRENT//./\\.}"
PYTHON="${PYTHON:-$(command -v python3 || command -v python)}"
[ -n "$PYTHON" ] || { echo "python3 is needed for the fake docker" >&2; exit 2; }
WORK="$(mktemp -d "${TMPDIR:-/tmp}/strategy-deploy-tests-XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
BIN="$WORK/bin"
mkdir -p "$BIN"
cp "$REPO/tests/deploy/fake-docker/fake_docker.py" "$BIN/"
printf '#!/bin/sh\nexec "%s" -I "%s" "$@"\n' "$PYTHON" "$BIN/fake_docker.py" > "$BIN/docker"
chmod 755 "$BIN/docker"
NON_ASCII="$(printf '\331\205\331\204\331\201-\303\251')"   # Arabic letters and e-acute (UTF-8)

FAILURES=0
COUNT=0
TEST=""
ok() {
  COUNT=$((COUNT + 1))
  if [ "$1" = 0 ]; then
    printf '    ok    %s\n' "$2"
  else
    printf '    FAIL  %s\n' "$2"
    FAILURES=$((FAILURES + 1))
    SHOW=1
  fi
}
check() { # check "<message>" <command...>
  local message="$1"
  shift
  if "$@" > /dev/null 2>&1; then ok 0 "$message"; else ok 1 "$message"; fi
}
start() { TEST="$1"; SHOW=0; printf '  %s\n' "$TEST"; }
show_on_failure() {
  if [ "$SHOW" = 1 ]; then
    printf '    --- script output ---\n'
    cat "$OUT"
    printf '    --- docker calls ---\n'
    cmds | sed 's/^/    /'
  fi
}

new_release() { # new_release <name> [broken-git]
  local root="$WORK/releases/$1-$NON_ASCII"
  mkdir -p "$root"
  cp -R "$REPO/deploy" "$root/deploy"
  rm -f "$root/deploy/.env" "$root/deploy/build-ca.local.pem"
  if [ "${2:-}" = broken-git ]; then printf 'gitdir: %s\n' "$WORK/no-such-repository" > "$root/.git"; fi
  printf '%s' "$root"
}

# run <release> <script> [args...]: FAIL (FAKE_DOCKER_FAIL) and EXTRA (space-separated KEY=VALUE words) may be set for one call.
STATE=""
OUT=""
CODE=0
SHOW=0
run() {
  local release="$1" script="$2"
  shift 2
  STATE="$(mktemp -d "$WORK/state-XXXXXX")"
  OUT="$STATE/output.txt"
  # shellcheck disable=SC2086 # EXTRA is a list of KEY=VALUE words
  env -u APP_VERSION -u NEW_VERSION -u GIT_REVISION \
    PATH="$BIN:$PATH" FAKE_DOCKER_STATE="$STATE" FAKE_DOCKER_FAIL="${FAIL:-}" \
    APP_PORT=18431 POSTGRES_PORT=15431 GOTENBERG_PORT=13431 ${EXTRA:-} \
    bash "$release/deploy/$script" "$@" < /dev/null > "$OUT" 2>&1
  CODE=$?
}
cmds() { [ -f "$STATE/calls.jsonl" ] && "$PYTHON" -I -c 'import json,sys
for l in open(sys.argv[1]): print(json.loads(l)["cmd"])' "$STATE/calls.jsonl"; }
has_call() { cmds | grep -Fxq -- "$1"; }
call_env() { # call_env <cmd> <variable>: value of the variable seen by the first call
  "$PYTHON" -I -c 'import json,sys
for l in open(sys.argv[1]):
    c = json.loads(l)
    if c["cmd"] == sys.argv[2]:
        print(c["env"][sys.argv[3]]); break' "$STATE/calls.jsonl" "$1" "$2"
}
utf8_lf() { # no BOM, no CR, valid UTF-8
  "$PYTHON" -I -c 'import sys
b = open(sys.argv[1], "rb").read()
b.decode("utf-8")
sys.exit(1 if b.startswith(b"\xef\xbb\xbf") or b"\r" in b else 0)' "$1"
}
env_line() { awk -F= -v k="$2" '$1 == k { sub(/^[^=]*=/, ""); v = $0 } END { printf "%s", v }' "$1/deploy/.env"; }
grep_out() { grep -Eq -- "$1" "$OUT"; }
sums_ok() { (cd "$1" && sha256sum --quiet -c SHA256SUMS); }

echo "Deploy script tests (bash $BASH_VERSION, fake docker via $PYTHON)"

start "install (fresh, release folder without .git)"
REL="$(new_release main)"
run "$REL" install.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "docker info checked" has_call "info"
check "image built (compose build migrate)" has_call "compose build migrate"
check "no .git: GIT_REVISION empty" test -z "$(call_env 'compose build migrate' GIT_REVISION)"
check "APP_VERSION from deploy/VERSION" test "$(call_env 'compose build migrate' APP_VERSION)" = "$CURRENT"
check "services started" has_call "compose up -d --no-build"
check ".env is UTF-8 without BOM with LF" utf8_lf "$REL/deploy/.env"
check "generated POSTGRES_PASSWORD" grep -Eq '^POSTGRES_PASSWORD=[0-9a-f]{48}$' "$REL/deploy/.env"
check "APP_PORT saved" test "$(env_line "$REL" APP_PORT)" = 18431
check "non-ASCII line of .env.example kept" grep -Fxq "$(grep -m1 -P '[^\x00-\x7F]' "$REL/deploy/.env.example")" "$REL/deploy/.env"
check "setup token printed" grep_out 'fake-setup-token-0163'
show_on_failure
PASSWORD="$(env_line "$REL" POSTGRES_PASSWORD)"

start "install again (keeps .env)"
run "$REL" install.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "password unchanged" test "$(env_line "$REL" POSTGRES_PASSWORD)" = "$PASSWORD"
show_on_failure

start "backup"
run "$REL" backup.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
BACKUP="$(find "$REL/backups" -mindepth 1 -maxdepth 1 -type d | head -n 1)"
check "backup folder strategy-<version>-<UTC>" test -n "$(basename "${BACKUP:-x}" | grep -E "^strategy-${CURRENT_RE}-[0-9]{8}T[0-9]{6}Z\$")"
check "database.dump byte-identical" cmp "$BACKUP/database.dump" "$STATE/emitted-pg_dump.bin"
check "files.tar.gz byte-identical" cmp "$BACKUP/files.tar.gz" "$STATE/emitted-files.tar.gz"
check "SHA256SUMS verifies" sums_ok "$BACKUP"
check "application stopped and started again" test "$(cmds | grep -cxF -e 'compose stop app' -e 'compose up -d --no-build --no-deps app')" = 2
show_on_failure

start "restore (verified)"
run "$REL" restore.sh "$BACKUP" --yes
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "pg_restore received the dump byte-identical" cmp "$STATE/restored.dump" "$BACKUP/database.dump"
check "files restore received the archive" cmp "$STATE/restored-files.tar.gz" "$BACKUP/files.tar.gz"
check "database recreated" grep -Fq 'DROP DATABASE IF EXISTS "strategy" WITH (FORCE);' "$STATE/psql.sql"
check "audit chain verified" has_call "compose run --rm --no-deps app --verify-audit-chain"
show_on_failure

start "restore of a damaged backup"
DAMAGED="$WORK/damaged-$NON_ASCII"
cp -R "$BACKUP" "$DAMAGED"
printf 'TAMPERED=1\n' >> "$DAMAGED/env"
run "$REL" restore.sh "$DAMAGED" --yes
check "exit code 7 (was $CODE)" test "$CODE" = 7
check "nothing stopped" test "$(cmds | grep -cxF 'compose stop app')" = 0
show_on_failure

start "upgrade to $NEXT (release folder without .git)"
EXTRA="NEW_VERSION=$NEXT" run "$REL" upgrade.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "new image built as $NEXT" test "$(call_env 'compose build migrate' APP_VERSION)" = "$NEXT"
check "no revision" test -z "$(call_env 'compose build migrate' GIT_REVISION)"
check "migrations as the owner" has_call "compose run --rm migrate"
check "APP_VERSION=$NEXT saved" test "$(env_line "$REL" APP_VERSION)" = "$NEXT"
check "pre-upgrade backup written" test "$(find "$REL/backups" -mindepth 1 -maxdepth 1 -type d | wc -l)" = 2
show_on_failure

start "upgrade with failing migrations"
FAIL="compose run --rm migrate=3" EXTRA="NEW_VERSION=$AFTER" run "$REL" upgrade.sh --no-build
check "exit code 7 (was $CODE)" test "$CODE" = 7
check "rollback command printed" grep_out 'restore\.sh .* --yes'
check "APP_VERSION unchanged" test "$(env_line "$REL" APP_VERSION)" = "$NEXT"
show_on_failure

start "install with a broken .git (git fails on stderr)"
if command -v git > /dev/null 2>&1; then
  BROKEN="$(new_release broken-git broken-git)"
  run "$BROKEN" install.sh
  check "exit code 0 (was $CODE)" test "$CODE" = 0
  check "failed rev-parse = no revision" test -z "$(call_env 'compose build migrate' GIT_REVISION)"
  show_on_failure
else
  echo "    skipped: git is not installed"
fi

start "Docker not running"
OTHER="$(new_release other)"
FAIL="info=1" run "$OTHER" install.sh
check "exit code 10 (was $CODE)" test "$CODE" = 10
check "nothing written" test ! -f "$OTHER/deploy/.env"
show_on_failure

echo ""
if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES of $COUNT assertions failed."
  exit 1
fi
echo "All $COUNT assertions passed."
