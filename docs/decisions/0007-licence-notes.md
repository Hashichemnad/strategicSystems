# 0007 — Licence notes: ClamAV sidecar and dev-only tooling licences

- **Status:** Accepted by the product owner (22 Oct 2026). Raised under CLAUDE.md §4.3 ("anything else → stop and write an ADR asking the owner").
- **Context:** Two cases fall outside the plain allow-list (MIT, Apache-2.0, BSD, ISC, MPL-2.0, Unlicense, OFL).

## 1. ClamAV (GPL-2.0), optional virus scanning
CLAUDE.md §4.4 lists ClamAV as an optional service. The application never links, embeds or ships ClamAV code:
- It runs as a **separate, optional container** (`docker compose --profile antivirus`), using the upstream image unchanged.
- The application talks to it over the network (clamd protocol) through an `IFileScanner` abstraction (Phase 3).
  It works without it (scanning off) or with any other scanner.

GPL obligations attach to distributing ClamAV itself, not to software that talks to it over a socket.
Customers who enable the profile pull the upstream image themselves.
**Decision:** keep ClamAV as an optional, customer-enabled sidecar that is never bundled.

## 2. Permissive licences used only by build / test tooling (never shipped)
The npm lockfile contains a few dev-only packages under licences that are not on the list, though all are permissive:

| Licence | Package (dev only) | Note |
|---|---|---|
| BlueOak-1.0.0 | `minimatch` (ESLint / tooling) | OSI-approved, permissive |
| Python-2.0 | `argparse` (via tooling) | OSI-approved, permissive |
| CC-BY-4.0 | `caniuse-lite` (browser data for the CSS build) | data licence, attribution |
| CC0-1.0 | `language-subtag-registry` (test tooling) | public-domain dedication |

`web/scripts/lib/licenses.mjs` accepts these **only for `dev: true` packages**. The same licence on a runtime dependency fails the check.
**Decision:** accept these four licences for dev tooling only. Any other licence outside the allow-list, or any of these on a runtime dependency, still needs a new ADR.
