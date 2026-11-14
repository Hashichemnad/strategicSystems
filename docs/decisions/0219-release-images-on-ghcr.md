# 0219 — Release images on ghcr.io and an offline archive; the scripts pull by default

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance. Supersedes the build-only parts of ADR 0157 (*Image and compose*:
  `pull_policy: never` with the build section in `docker-compose.yml`; the scripts' default "build the image").
- **Context:** Owner requirement (release 1.0.2, fix 5): "release images on ghcr.io + offline .tar, scripts pull by default". Until 1.0.1
  every customer built `strategy-app:<version>` on the server (10–20 minutes, about 10 GB of disk, npm / NuGet / apt downloads that fail on
  slow or inspected networks, ADR 0163) and nothing was published; offline machines needed a `docker save` made by hand on another
  installation. Owner decision: the ghcr.io package is **public** (anonymous pulls; customers need no GitHub account or token).

## Decision

### Publishing (`.github/workflows/release.yml`)
- Runs on a pushed tag `v*` and on *Run workflow* (inputs `version`, default `deploy/VERSION`, and `push`, default false). A tag or
  `push: true` publishes and requires the version to equal `deploy/VERSION` (the scripts pull `<image>:<deploy/VERSION>`); a dispatch
  without `push` is a dry run whose assets are kept as a workflow artifact for 7 days.
- Builds `deploy/Dockerfile` exactly like the CI `docker` job (build arguments `APP_VERSION`, `GIT_REVISION` = short commit,
  `BUILD_CREATED`; OCI labels from the Dockerfile, whose `source` label links the package to the repository), checks non-root /
  healthcheck / version and revision labels, runs `deploy/sbom.sh --scan` (Trivy gate and CycloneDX SBOM, ADR 0156).
- Pushes `ghcr.io/<owner lower-case>/strategy-app:<version>` with the workflow's `GITHUB_TOKEN` (`packages: write`), and `:latest` only
  for a final version (no `-<pre-release>` suffix). Saves the same image with `docker save | gzip` as `strategy-app-<version>.tar.gz` plus
  `strategy-app-<version>.tar.gz.sha256` (sha256sum format) and attaches them and the SBOM to the GitHub release `v<version>` with the
  `gh` CLI (created when missing, with the version's CHANGELOG section and install notes; existing assets replaced). No third-party
  actions: plain `docker` / `gh` commands like the rest of the CI (portable to Azure DevOps).
- The package is created private by GitHub on the first push; the owner makes it public **once** (Packages → `strategy-app` → Package
  settings → Change visibility). Documented in `deploy/README.md` and both installation guides.

### Compose
- `migrate` and `app` use `image: ${APP_IMAGE:-ghcr.io/hashichemnad/strategy-app}:${APP_VERSION:-<release>}` with **`pull_policy: missing`**:
  pulled when the tag is not on the machine, otherwise reused (so `run --rm migrate` / `run --rm app --create-admin` never pull or rebuild,
  the concern ADR 0157 solved with `never`). `APP_IMAGE` (in `.env.example` and written to `deploy/.env` by install / upgrade) points to a
  customer registry mirror.
- The build section, the `build_ca` secret and `pull_policy: never` move to the override **`deploy/docker-compose.build.yml`**, used only
  when building from source (`-f deploy/docker-compose.yml -f deploy/docker-compose.build.yml`). The built image keeps the release name, so
  every later command works without the override. CI checks `compose config -q` with and without it.

### Scripts (`install` / `upgrade`, `.sh` and `.ps1` with the same behaviour)
| Option | Behaviour |
|---|---|
| *(default)* | `docker compose pull migrate app` for `<APP_IMAGE>:<APP_VERSION>`. A failed pull uses a local copy of the same tag with a warning; without one it stops before anything changes (exit 4) and explains `--image` (offline archive), `APP_IMAGE` (mirror) and `--build` |
| `--image` / `-Image <file>` | `.tar` or `.tar.gz`. `<file>.sha256` next to it is verified first (sha256sum / Get-FileHash; mismatch → exit 4, nothing loaded; a missing file is a warning). After `docker load`, `<APP_IMAGE>:<version>` must exist; an image of another repository with the same tag listed by `docker load` (`Loaded image: …strategy-app:<version>`: the ghcr.io name for a mirror `APP_IMAGE`, or `strategy-app:<version>` of a source build) is tagged with it |
| `--build` / `-Build` | the former default: `compose … -f docker-compose.build.yml build migrate` with `BUILD_CA_FILE`, `GIT_REVISION`, `BUILD_CREATED` |
| `--no-pull` / `-NoPull` | the image must already be on the machine (exit 4 otherwise). `--no-build` / `-NoBuild` remain as deprecated aliases with a warning |
- The options are mutually exclusive (exit 2). Upgrade gets the new image first, while the current version runs, as before; the rollback
  message names the previous image `<APP_IMAGE>:<old version>`. `restore` pulls the image of `APP_VERSION` when it is not on the machine.
- **Installations of 1.0.0 / 1.0.1** have no `APP_IMAGE` and a local image `strategy-app:<version>`: `upgrade` writes the default
  `APP_IMAGE` to `.env` and tags the old local image with the new name before the pre-upgrade backup, so the backup (which runs the old
  image), a rollback and `restore` with `APP_VERSION=<old>` keep working offline. `--no-pull` and a failed pull do the same for a local
  `strategy-app:<version>`.
- `sbom.sh` defaults to `<APP_IMAGE>:<APP_VERSION>`; the SBOM file name stays `strategy-app-<version>.cdx.json`.
- Tests: the fake docker (ADR 0163) tracks images when `FAKE_DOCKER_IMAGES` is set (load / tag / pull / build add images, `image inspect`
  fails for others), reports the `Loaded image` of `FAKE_DOCKER_LOAD_IMAGE` and records the compose files of each call. The bash and
  PowerShell suites cover pull by default, `--image .tar.gz` with a matching and a mismatching checksum, tagging for a mirror and for a
  legacy archive, `--build` with the override, `--no-pull` with and without the image, the `--no-build` alias, a failed pull, conflicting
  options and the upgrade of a pre-1.0.2 installation.

## Consequences
- Install and upgrade take minutes instead of a full build, need no build tools, caches or package-registry access on the server, and run
  the image that was scanned and given an SBOM in the release workflow (byte-identical between registry and archive).
- Servers need outbound HTTPS to `ghcr.io` (and Docker Hub for PostgreSQL / Gotenberg), or use the offline archive or a mirror.
- The image is publicly downloadable (owner decision): anyone can pull and inspect it, although the product licence still applies.
  Nothing secret is in the image (secrets are in `deploy/.env` and the database, ADR 0157).
- Building from source stays available and tested (`--build`), e.g. for `INSTALL_OCR=false` or customer-built images.
- A release is published by pushing `v<deploy/VERSION>`; a tag that does not match `deploy/VERSION` fails the workflow.
