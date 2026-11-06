# 0156 — Performance, image-scanning and SBOM tools (external, never distributed)

- **Status:** Proposed (Phase 18)
- **Context:** Phase 18 needs a load test against the NFRs, container image vulnerability scanning (no High / Critical) and a software bill
  of materials of the release image. CLAUDE.md §4.3 allows only free / OSI-licensed **dependencies** of the product. These tools are not
  dependencies: they are run by the build / test pipeline against the product and are never part of an image, the repository's packages or
  a customer delivery.

## Decision
| Tool | Licence | Use | How it runs |
|---|---|---|---|
| **k6** (Grafana) | AGPL-3.0 | Load tests in `tests/perf/` | `grafana/k6` container (`mirror.gcr.io/grafana/k6`) on the test machine; scripts are our own (plain JS, no k6 code copied) |
| **Trivy** (Aqua Security) | Apache-2.0 | Image and OS-package vulnerability scan; fails on High / Critical with a fix available | `aquasec/trivy` container in CI (`docker` job) and `deploy/sbom.sh --scan` locally |
| **Syft** (Anchore) | Apache-2.0 | CycloneDX 1.6 JSON SBOM of `strategy-app:<version>` → `release/sbom/strategy-app-<version>.cdx.json` | `anchore/syft` container (`deploy/sbom.sh`, CI artifact) |

- AGPL-3.0 obligations attach to distributing or offering k6 itself over a network; we do neither (it runs only on our test machines, the
  customer never receives it). It is recorded here and in `docs/THIRD_PARTY_LICENSES.md` under *tools, not distributed*.
- NBomber was considered and rejected: its licence requires a paid plan for organisational / commercial use.
- `dotnet list package --vulnerable` (`tools/check-vulnerable.sh`) and `npm audit --audit-level=high` stay the dependency gates; Trivy adds
  the OS packages of the runtime image (Debian, Tesseract) and the published files.

## Consequences
- No new product dependency, no licence change for customers.
- CI pulls three images (pinned by version in the workflow) — in restricted networks through `REGISTRY_PREFIX` / a mirror.
- The SBOM is regenerated for every release image and published with the release (CI artifact; the 1.0.0 file is committed).
