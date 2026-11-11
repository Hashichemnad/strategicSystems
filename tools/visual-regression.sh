#!/usr/bin/env bash
# Visual-regression check per theme (ADR 0175) inside a pinned Playwright container, so the browser, the system libraries and the fonts
# are identical on every machine and in CI. The product fonts (IBM Plex Sans, IBM Plex Sans Arabic, Noto Kufi Arabic) are installed into
# the container from the locked npm packages (web/node_modules) — no network access, no font drift.
#
# Needs: a running host with a fresh Development database (E2E_BASE_URL, default http://localhost:5080), `npm ci` in web/ on Linux x64.
#   tools/visual-regression.sh                     # compare with the committed baselines (web/e2e/visual.spec.ts-snapshots/)
#   UPDATE_SNAPSHOTS=1 tools/visual-regression.sh  # rewrite the baselines, then review and commit them
set -euo pipefail
cd "$(dirname "$0")/.."

# Pinned by digest: mcr.microsoft.com/playwright:v1.56.1-noble (Chromium 1194 = @playwright/test 1.56.1 in web/package.json).
IMAGE="${VISUAL_IMAGE:-mcr.microsoft.com/playwright@sha256:f1e7e01021efd65dd1a2c56064be399f3e4de00fd021ac561325f2bfbb2b837a}"
BASE_URL="${E2E_BASE_URL:-http://localhost:5080}"

docker run --rm --network host --ipc=host \
  -v "$PWD:/work" -w /work/web \
  -e E2E_VISUAL=1 -e E2E_BASE_URL="$BASE_URL" -e UPDATE_SNAPSHOTS="${UPDATE_SNAPSHOTS:-}" -e CI="${CI:-}" \
  -e HOST_UID="$(id -u)" -e HOST_GID="$(id -g)" \
  "$IMAGE" bash -euo pipefail -c '
    mkdir -p /usr/share/fonts/product
    cp node_modules/@fontsource/ibm-plex-sans/files/*.woff2 \
       node_modules/@fontsource/ibm-plex-sans-arabic/files/*.woff2 \
       node_modules/@fontsource/noto-kufi-arabic/files/*.woff2 /usr/share/fonts/product/
    fc-cache -f >/dev/null
    status=0
    npx playwright test --project visual-ar --project visual-en --trace off --reporter=list || status=$?
    # Files written in the container belong to the caller, not root.
    chown -R "$HOST_UID:$HOST_GID" e2e/visual.spec.ts-snapshots test-results 2>/dev/null || true
    exit $status
  '
