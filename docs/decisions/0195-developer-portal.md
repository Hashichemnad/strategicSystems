# 0195 — Developer portal page and Power BI guide

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §F: a developer page with OpenAPI docs, OData metadata, webhook catalogue with sample payloads and how to
  connect Power BI.

## Decision
- SPA page Settings → Integrations → Developer portal (`integration.portal.view`, roles `integration.administrator` and the new
  `integration.developer`). Content: links to `/openapi`, the OData endpoints of each system with the metadata document rendered **for the
  viewer** (`GET /api/v1/integration/portal/metadata/{system}`), extract endpoints, the webhook catalogue with sample payloads and signature
  verification snippets, and the Power BI / Excel guide in Arabic and English (also `docs/guides/{en,ar}/power-bi.md`).
- The portal shows only metadata; data is read only by integration clients.

## Consequences
Developers see the same model a client with the same roles would see; nothing is reachable anonymously.
