# 0083 — Document sensitivity clearance: one visibility rule for every read path

- **Status:** Accepted by the owner (Phase 14, before the pull request)
- **Context:** SPEC §6.4–6.5: the library shows only documents the user's sensitivity clearance allows; sensitivity levels are mapped to
  roles / units in Settings (e.g. Confidential → owner unit + Strategy Office + leadership); published Internal documents are visible to all
  employees; "Share to all employees?" on publish (otherwise visibility follows sensitivity). Q-RQ2: Public · Internal · Confidential ·
  Secret. CLAUDE.md §7: lists filtered in the database query; 404 outside scope. Search, Ask and MCP must not leak.

## Decision
- Sensitivity levels are a lookup (`docreg.sensitivity`); clearance per level in Settings → Document Registry: `allEmployees`, `ownerUnit`
  (owner unit and below), `roleKeys[]`, `allowShareToAll`. Seed: Public, Internal → all employees; Confidential → owner unit +
  `docreg.office` + `docreg.leadership`; Secret → `docreg.office` + `docreg.leadership`, share-to-all not allowed. A new level without a
  rule is visible to the office only (safest).
- A published / expired document is visible when share-to-all, or cleared for its level, or the user is its owner / a version requester,
  or holds `docreg.document.manage` (All). Withdrawn and superseded content is visible to the office and the owner only (archive).
- `DocumentVisibility` builds the SQL predicate once per request and is the **only** read path: library, page, versions, compare, file
  download hook, dashboard counts, search source (global search and Ask), MCP tools, AI Q&A and duplicate check, Links badges and
  `IDocumentDirectory`. Outside it: 404.
- Public links for Public documents are an optional setting (off); not built in Phase 14 beyond the setting (no anonymous endpoint yet).

## Consequences
Changing a clearance rule takes effect immediately for every path. Tests prove each path with the same users.
