# 0143 — Widget catalogue, dashboard definitions, role defaults and builder security

- **Status:** Accepted by the owner (Phase 17)
- **Context:** SPEC §0.4 dashboard builder: admins arrange default dashboards per role and publish without code; users pin favourites and
  save filters; users can only build on data they may see.

## Decision
- Widget types: tile, bar, line, bubble, heat-map (accessible grid), table, list, strategy map, RAG grid, gauge. Config is JSON validated
  server-side against the metric / list catalogue.
- Tables `reporting.dashboard` (level system / executive / role / custom, draft → published versions), `widget`, `dashboard_role`
  (role defaults), `dashboard_share`, `user_pin`, `saved_filter`, `my_work_layout`.
- The builder lists only metrics whose permission the builder holds. Viewers see only widgets whose metric permission they hold, with
  their own scope. Definitions carry no data. Seeded system / executive / role dashboards are editable; re-seeding never overwrites an
  edited copy.

## Consequences
Level 3 system dashboards and the executive dashboard are data, rendered by one generic page; customers change them without code.
