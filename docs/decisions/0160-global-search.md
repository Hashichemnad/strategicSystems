# 0160 — Global search: module contributors, Links-registry resolution, generated search columns

- **Status:** Proposed (Phase 19, stream S) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §A1 asks for one search box over every system the user can access, grouped by system, Arabic-normalised,
  permission-filtered with 404 semantics, with recent items. Eleven modules own 22 linkable record types; only three tables had a
  normalised search column and none had a trigram index on it. Search must never reveal a record the user may not read, not even as a count.

## Decision
- New platform component **`Strategy.Platform.Search`** (schema **`search`**, endpoints `/api/v1/search`) with the contract project
  `Strategy.Platform.Search.Contracts`. Each module implements **`ISearchContributor`** (registered with its system code) over its own
  scoped queries; a contributor registered without a system code (external references, stream X) is cross-system.
- **Two permission layers.** (1) The contributor filters by the user's data scope in the database query, so the top-N is made of readable
  records. (2) The service resolves every hit through **`ILinkRegistry.ResolveAsync`** and keeps only `visible` views; number, title,
  status and link come from the owning module's link source. A contributor bug therefore cannot leak content. Contributors of systems the
  user cannot open (`IUserRoleQuery.OpenSystemsAsync`: enabled and granted) are not called.
- **Arabic normalisation in the database by a generated column.** `quick_search text GENERATED ALWAYS AS (<SQL twin of
  SearchText.Normalise>) STORED` over number + titles + key fields, with a GIN `gin_trgm_ops` index (`ILIKE '%term%'` and `similarity`
  are index-assisted). The SQL uses only immutable functions (`regexp_replace`, `translate`, `lower`, `btrim`, `coalesce`, `||`). A
  generated column cannot drift from the row and needs no backfill code. Existing C#-maintained columns (Requests `title_search`, Document
  Registry and IP `search_text`) are reused and only indexed. Computed columns are not audited (derived data).
- **Ranking** is a pure C# function (`SearchRanking`): exact number > number prefix > number contains > title (0.5 + 0.2 × trigram
  similarity) > key field (0.3 + 0.1 × similarity, with a snippet).
- **Recent items** are stored server-side per user (`search.recent_item`, ≤ 20, upsert + trim) and re-resolved through the Links registry on
  every read, so revoked access or a switched-off system removes them. They are navigation history, not business records: not audited,
  the oldest rows are hard-deleted when trimming.
- Search reads are not audited (reads policy, CLAUDE.md §8); the MCP / Ask tool `search` is audited by the tool gateway like every tool call.

## Consequences
- Twelve module migrations add the column / index (nine generated columns, three indexes on existing columns). Adding a searchable field
  later is a migration that redefines the generated column.
- The generated SQL ignores Latin combining marks (the C# function drops them); terms are normalised in C#, so a combining mark in a
  stored Latin title is the only case where SQL and C# differ (an accent typed by the user is removed before matching).
- If a searched text field becomes rich text (stream R), its HTML tags end up in `quick_search`; such a field should then be searched
  through its plain-text projection (redefine the generated column on that column).
- Phase 20 reuses the `CommandSearch` component as the Modern theme's command palette.
