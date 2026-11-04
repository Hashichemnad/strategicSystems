# 0088 — Request PDF templates and the embeddable submit page

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §5.1: template-based bilingual PDF generation with merge fields; optional deep links / embeddable "submit request" pages
  for the intranet using the same SSO login.

## Decision
- Templates are bilingual HTML stored on the type, with an in-house mustache-like syntax (`{{field.x}}`, `{{#rows table}}…{{/rows}}`,
  `{{process.stage.field}}`, `{{request.number}}`, `{{org.name}}`, `{{today}}`). Every value is HTML-encoded; richtext values are sanitised
  first. No scripts: the renderer has no expressions or helpers beyond loops. PDFs through the Phase 3 `IPdfRenderer` (Gotenberg).
  Secret fields render masked in request templates (the MOET form is an IP-module template, ADR 0086).
- Deep link `/requests/new/{type}` and a chrome-less page `/embed/requests/{type}` for types flagged *embeddable*. Same cookie
  authentication and SSO redirect; the host sends `Content-Security-Policy: frame-ancestors` with the origins listed in Settings → Requests
  for `/embed/*` only (default `'self'`).

## Consequences
No anonymous submission. Organisations that embed in another origin must list it explicitly; cookies need `SameSite=None` in that case,
which the install guide documents (Phase 18).
