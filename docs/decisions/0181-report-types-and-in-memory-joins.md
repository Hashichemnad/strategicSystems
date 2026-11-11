# 0181 — Report types and joins in the reporting layer

- **Status:** Accepted (Phase 21)
- **Context:** E1: a primary record type + related types up to 3 levels, inner ("with") / outer ("with or without") joins, seeded per
  system and editable by admins; permissions on every row and related row; paging, limits.

## Decision
- `reporting.report_type` (jsonb definition: nodes with alias, dataset, relationship, parent, join, offered fields). Tree up to 3 related
  levels, 6 objects; siblings allowed. Every dataset is also an implicit single-object type `dataset:<code>`.
- Run: primary rows from the module (≤ `ReportPrimaryRowLimit`, default 10 000), then each related node in chunks of 500 keys through the
  module's scoped query; join in memory. A parent is repeated per related row; **siblings are listed one after another**, never multiplied
  (a project with 3 risks and 2 KPIs gives 5 rows, not 6). Composition limit `ExportRowLimit`; sorting, grouping, paging on the composed rows;
  whole run under `ReportTimeoutSeconds` (default 30).
- Seeded types are re-seeded only while unedited (hash), like dashboards (ADR 0143). Admin editing needs `reporting.report-type.manage`.
- An unreadable related node: no fields; as inner join the report is empty, as outer its columns stay empty.

## Consequences
No generic SQL and no cross-schema joins; large primary sets are bounded and flagged `truncated`. Paging re-runs the queries (no server-side
result cache) — acceptable at the limits; a result cache can be added later without contract change.
