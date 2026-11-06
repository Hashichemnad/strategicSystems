#!/usr/bin/env bash
# CycloneDX SBOM of the release image with Syft, and optionally a Trivy vulnerability scan (ADR 0156). Both run as containers; nothing is
# installed on the machine and nothing is added to the image.
#
#   ./deploy/sbom.sh                       # → release/sbom/strategy-app-<version>.cdx.json
#   ./deploy/sbom.sh --scan                # also: Trivy scan, fails (exit 1) on High / Critical vulnerabilities that have a fix
#   IMAGE=strategy-app:1.0.0 ./deploy/sbom.sh
set -euo pipefail
# shellcheck source=deploy/lib.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

SCAN=false
[ "${1:-}" = "--scan" ] && SCAN=true
VERSION="$(setting APP_VERSION "$(release_version)")"
IMAGE="${IMAGE:-strategy-app:$VERSION}"
PREFIX="$(setting REGISTRY_PREFIX)"
SYFT_IMAGE="${SYFT_IMAGE:-${PREFIX}anchore/syft:v1.57.0}"
TRIVY_IMAGE="${TRIVY_IMAGE:-${PREFIX}aquasec/trivy:0.76.2}"
OUT_DIR="$ROOT_DIR/release/sbom"

require_docker
docker image inspect "$IMAGE" > /dev/null 2>&1 || die "Image $IMAGE not found: build it first (./deploy/install.sh or docker compose … build)." 4
mkdir -p "$OUT_DIR"
OUT="$OUT_DIR/${IMAGE//[:\/]/-}.cdx.json"

info "SBOM of $IMAGE (CycloneDX JSON, Syft)"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock:ro "$SYFT_IMAGE" \
  "docker:$IMAGE" -o cyclonedx-json --quiet > "$OUT"
say "  $OUT ($(wc -c < "$OUT" | tr -d ' ') bytes, $(grep -o '"bom-ref"' "$OUT" | wc -l | tr -d ' ') components)"

if [ "$SCAN" = true ]; then
  info "Vulnerability scan of $IMAGE (Trivy: HIGH, CRITICAL with a fix available)"
  # Behind a TLS-inspecting proxy the vulnerability database download needs the organisation's CA bundle (same file as the build's).
  ca_args=()
  ca="$(setting BUILD_CA_FILE)"
  case "$ca" in ./*) ca="$DEPLOY_DIR/${ca#./}" ;; esac
  if [ -n "$ca" ] && [ -s "$ca" ]; then ca_args=(-v "$ca:/run/build-ca.pem:ro" -e SSL_CERT_FILE=/run/build-ca.pem); fi
  docker run --rm "${ca_args[@]}" -v /var/run/docker.sock:/var/run/docker.sock:ro -v "${TRIVY_CACHE:-strategy-trivy-cache}:/root/.cache/trivy" "$TRIVY_IMAGE" \
    image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --no-progress "$IMAGE"
fi
