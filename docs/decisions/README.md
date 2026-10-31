# Architecture decision records

One file per decision, `NNNN-title.md`, numbered in order. Status: Proposed · Accepted · Superseded by NNNN.
Write an ADR for every choice the SPEC or CLAUDE.md leaves open (CLAUDE.md §2 rule 9) and list it in the phase summary.

| # | Decision | Status |
|---|---|---|
| [0001](0001-minimal-apis.md) | Endpoint style: Minimal APIs grouped per module | Accepted |
| [0002](0002-tanstack-router.md) | Front-end router: TanStack Router | Accepted |
| [0003](0003-in-house-dispatcher.md) | In-house command / query dispatcher | Accepted |
| [0004](0004-openapi-ts-client.md) | OpenAPI → TypeScript client pipeline | Accepted |
| [0005](0005-phase-0-access-placeholder.md) | Phase 0 access placeholder that fails closed | Superseded by 0009 (Phase 1) |
| [0006](0006-module-pattern.md) | Module pattern, persistence and extra platform projects | Accepted |
| [0007](0007-licence-notes.md) | Licence notes: ClamAV sidecar and dev-only tooling licences | Accepted |
| [0008](0008-test-and-tooling-versions.md) | Test runner and front-end tool versions | Accepted |
| [0009](0009-interim-system-admin-access.md) | Interim access: signed-in users and a system-administrator flag | Superseded by 0016 (Phase 2) |
| [0010](0010-platform-components-and-identity-schema.md) | Platform components with endpoints, and the `identity` schema | Accepted |
| [0011](0011-runtime-identity-providers.md) | Runtime-configured identity providers and account linking | Accepted |
| [0012](0012-data-protection-and-secrets.md) | Data Protection key storage and secret encryption | Accepted |
| [0013](0013-org-history-model.md) | Organisation history: effective-dated versions and a materialised path | Accepted |
| [0014](0014-interim-placements-phase-1.md) | Interim placements until later platform services exist | Accepted (temporary; grade and settings screens closed in Phase 2) |
| [0015](0015-data-scope-semantics.md) | Data scope semantics and how scopes reach the database | Accepted |
| [0016](0016-roles-replace-interim-admin.md) | Roles replace the interim system-administrator flag | Accepted |
| [0017](0017-settings-storage-and-numbering.md) | Settings storage, numbering counters and interim branding assets | Accepted |
