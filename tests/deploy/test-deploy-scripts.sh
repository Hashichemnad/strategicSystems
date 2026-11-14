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
  env -u APP_VERSION -u NEW_VERSION -u GIT_REVISION -u APP_IMAGE -u FAKE_DOCKER_IMAGES -u FAKE_DOCKER_LOAD_IMAGE \
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
call_files() { # call_files <cmd>: compose files (-f) of the first call, space-separated base names
  "$PYTHON" -I -c 'import json,sys
for l in open(sys.argv[1]):
    c = json.loads(l)
    if c["cmd"] == sys.argv[2]:
        print(" ".join(c["files"])); break' "$STATE/calls.jsonl" "$1"
}
call_index() { cmds | grep -nFx -- "$1" | head -n 1 | cut -d: -f1; }
before() { local a b; a="$(call_index "$1")"; b="$(call_index "$2")"; [ -n "$a" ] && [ -n "$b" ] && [ "$a" -lt "$b" ]; }
no_call_like() { ! cmds | grep -Eq -- "$1"; }
first_dump() { cmds | grep -m1 '^compose exec -T postgres pg_dump '; }
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
GHCR="ghcr.io/hashichemnad/strategy-app"
GHCR_RE="${GHCR//./\\.}"
# Offline release archives (contents do not matter to the fake docker) with sha256sum-format checksum files, as release.yml publishes them.
ARCHIVES="$WORK/archives-$NON_ASCII"
mkdir -p "$ARCHIVES"
new_archive() { # new_archive <version> [bad-checksum] -> path of strategy-app-<version>.tar.gz next to its .sha256
  local file="$ARCHIVES/strategy-app-$1.tar.gz"
  printf '\037\213fake image archive %s\n' "$1" > "$file"
  if [ "${2:-}" = bad-checksum ]; then
    printf '%064d  %s\n' 0 "$(basename "$file")" > "$file.sha256"
  else
    (cd "$ARCHIVES" && sha256sum "$(basename "$file")" > "$(basename "$file").sha256")
  fi
  printf '%s' "$file"
}

start "install (fresh, release folder without .git)"
REL="$(new_release main)"
run "$REL" install.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "docker info checked" has_call "info"
check "release image pulled by default (compose pull migrate app)" has_call "compose pull migrate app"
check "APP_VERSION from deploy/VERSION" test "$(call_env 'compose pull migrate app' APP_VERSION)" = "$CURRENT"
check "APP_IMAGE = ghcr.io release package" test "$(call_env 'compose pull migrate app' APP_IMAGE)" = "$GHCR"
check "pulled without the build override" test "$(call_files 'compose pull migrate app')" = "docker-compose.yml"
check "nothing built" no_call_like '^compose build( |$)'
check "services started" has_call "compose up -d --no-build"
check "pull before start" before "compose pull migrate app" "compose up -d --no-build"
check "APP_IMAGE saved in .env" test "$(env_line "$REL" APP_IMAGE)" = "$GHCR"
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
check "new image pulled as $NEXT" test "$(call_env 'compose pull migrate app' APP_VERSION)" = "$NEXT"
check "nothing built" no_call_like '^compose build( |$)'
check "pull before the pre-upgrade backup" before "compose pull migrate app" "$(first_dump)"
check "migrations as the owner" has_call "compose run --rm migrate"
check "APP_VERSION=$NEXT saved" test "$(env_line "$REL" APP_VERSION)" = "$NEXT"
check "pre-upgrade backup written" test "$(find "$REL/backups" -mindepth 1 -maxdepth 1 -type d | wc -l)" = 2
show_on_failure

start "upgrade with failing migrations (--no-build, deprecated alias of --no-pull)"
FAIL="compose run --rm migrate=3" EXTRA="NEW_VERSION=$AFTER" run "$REL" upgrade.sh --no-build
check "exit code 7 (was $CODE)" test "$CODE" = 7
check "deprecation warning" grep_out '--no-build is deprecated: use --no-pull'
check "nothing pulled or built" no_call_like '^compose (pull|build)( |$)'
check "rollback command printed" grep_out 'restore\.sh .* --yes'
check "previous image named" grep_out "previous image $GHCR_RE:${NEXT//./\\.} is still on this machine"
check "APP_VERSION unchanged" test "$(env_line "$REL" APP_VERSION)" = "$NEXT"
show_on_failure

start "install --build (from source, build override)"
SRC="$(new_release source)"
run "$SRC" install.sh --build
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "image built (compose build migrate)" has_call "compose build migrate"
check "built with docker-compose.build.yml" test "$(call_files 'compose build migrate')" = "docker-compose.yml docker-compose.build.yml"
check "APP_VERSION from deploy/VERSION" test "$(call_env 'compose build migrate' APP_VERSION)" = "$CURRENT"
check "built as the release image name" test "$(call_env 'compose build migrate' APP_IMAGE)" = "$GHCR"
check "no .git: GIT_REVISION empty" test -z "$(call_env 'compose build migrate' GIT_REVISION)"
check "BUILD_CREATED in UTC" grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' <(call_env 'compose build migrate' BUILD_CREATED)
check "nothing pulled" no_call_like '^compose pull( |$)'
check "build before start" before "compose build migrate" "compose up -d --no-build"
show_on_failure

start "upgrade --build"
EXTRA="NEW_VERSION=$NEXT" run "$SRC" upgrade.sh --build --no-backup
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "new image built as $NEXT with the override" test "$(call_env 'compose build migrate' APP_VERSION) $(call_files 'compose build migrate')" = "$NEXT docker-compose.yml docker-compose.build.yml"
check "nothing pulled" no_call_like '^compose pull( |$)'
check "APP_VERSION=$NEXT saved" test "$(env_line "$SRC" APP_VERSION)" = "$NEXT"
show_on_failure

start "install --image <archive>.tar.gz (checksum verified, ghcr.io name inside)"
OFF="$(new_release offline)"
ARCHIVE="$(new_archive "$CURRENT")"
EXTRA="FAKE_DOCKER_IMAGES=none FAKE_DOCKER_LOAD_IMAGE=$GHCR:$CURRENT" run "$OFF" install.sh --image "$ARCHIVE"
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "checksum verified" grep_out 'Checksum verified'
check "archive loaded" has_call "load -i $ARCHIVE"
check "loaded image checked" has_call "image inspect $GHCR:$CURRENT"
check "no retag needed" no_call_like '^tag '
check "nothing pulled or built" no_call_like '^compose (pull|build)( |$)'
check "services started" has_call "compose up -d --no-build"
show_on_failure

start "upgrade --image <archive>.tar.gz (strategy-app:<version> inside is tagged)"
ARCHIVE_NEXT="$(new_archive "$NEXT")"
EXTRA="NEW_VERSION=$NEXT FAKE_DOCKER_IMAGES=$GHCR:$CURRENT FAKE_DOCKER_LOAD_IMAGE=strategy-app:$NEXT" run "$OFF" upgrade.sh --image "$ARCHIVE_NEXT"
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "checksum verified" grep_out 'Checksum verified'
check "archive loaded" has_call "load -i $ARCHIVE_NEXT"
check "tagged as $GHCR:$NEXT" has_call "tag strategy-app:$NEXT $GHCR:$NEXT"
check "nothing pulled or built" no_call_like '^compose (pull|build)( |$)'
check "load before the pre-upgrade backup" before "load -i $ARCHIVE_NEXT" "$(first_dump)"
check "APP_VERSION=$NEXT saved" test "$(env_line "$OFF" APP_VERSION)" = "$NEXT"
show_on_failure

start "install --image with a checksum mismatch (refused)"
MIRROR="$(new_release mirror)"
BAD="$(new_archive "$AFTER" bad-checksum)"
EXTRA="FAKE_DOCKER_IMAGES=none" run "$MIRROR" install.sh --image "$BAD"
check "exit code 4 (was $CODE)" test "$CODE" = 4
check "mismatch reported" grep_out 'Checksum mismatch'
check "nothing loaded" no_call_like '^load '
check "nothing started" no_call_like '^compose up'
show_on_failure

start "install --image for a registry mirror (APP_IMAGE): ghcr.io name tagged"
EXTRA="APP_IMAGE=registry.example.org/strategy/strategy-app FAKE_DOCKER_IMAGES=none FAKE_DOCKER_LOAD_IMAGE=$GHCR:$CURRENT" run "$MIRROR" install.sh --image "$ARCHIVE"
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "tagged with the mirror name" has_call "tag $GHCR:$CURRENT registry.example.org/strategy/strategy-app:$CURRENT"
check "APP_IMAGE saved" test "$(env_line "$MIRROR" APP_IMAGE)" = "registry.example.org/strategy/strategy-app"
show_on_failure

start "install --no-pull (image on this machine)"
LOCAL="$(new_release local)"
run "$LOCAL" install.sh --no-pull
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "image checked" has_call "image inspect $GHCR:$CURRENT"
check "nothing pulled or built" no_call_like '^compose (pull|build)( |$)'
check "services started" has_call "compose up -d --no-build"
show_on_failure

start "install --no-build (deprecated alias of --no-pull)"
run "$LOCAL" install.sh --no-build
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "deprecation warning" grep_out '--no-build is deprecated: use --no-pull'
check "nothing pulled or built" no_call_like '^compose (pull|build)( |$)'
show_on_failure

start "upgrade of a pre-1.0.2 installation (no APP_IMAGE, image built as strategy-app:<version>)"
sed -i '/^APP_IMAGE=/d' "$LOCAL/deploy/.env"
EXTRA="NEW_VERSION=$NEXT FAKE_DOCKER_IMAGES=strategy-app:$CURRENT" run "$LOCAL" upgrade.sh
check "exit code 0 (was $CODE)" test "$CODE" = 0
check "APP_IMAGE default written to .env" test "$(env_line "$LOCAL" APP_IMAGE)" = "$GHCR"
check "old image tagged with the release name" has_call "tag strategy-app:$CURRENT $GHCR:$CURRENT"
check "tagged before the pre-upgrade backup" before "tag strategy-app:$CURRENT $GHCR:$CURRENT" "$(first_dump)"
check "new image pulled as $NEXT" test "$(call_env 'compose pull migrate app' APP_VERSION)" = "$NEXT"
show_on_failure

start "install --no-pull without the image"
NOIMG="$(new_release no-image)"
EXTRA="FAKE_DOCKER_IMAGES=none" run "$NOIMG" install.sh --no-pull
check "exit code 4 (was $CODE)" test "$CODE" = 4
check "message names the image" grep_out "--no-pull: image $GHCR_RE:${CURRENT//./\\.} is not on this machine"
check "nothing started" no_call_like '^compose up'
show_on_failure

start "install when the pull fails (offline machine)"
FAIL="compose pull=1" EXTRA="FAKE_DOCKER_IMAGES=none" run "$NOIMG" install.sh
check "exit code 4 (was $CODE)" test "$CODE" = 4
check "points to --image" grep_out 'run the script again with --image <file>'
check "nothing started" no_call_like '^compose up'
show_on_failure

start "install with --build and --no-pull (refused)"
run "$NOIMG" install.sh --build --no-pull
check "exit code 2 (was $CODE)" test "$CODE" = 2
show_on_failure

start "install --build with a broken .git (git fails on stderr)"
if command -v git > /dev/null 2>&1; then
  BROKEN="$(new_release broken-git broken-git)"
  run "$BROKEN" install.sh --build
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
