# 0144 — Report builder: module datasets, field permissions, run-time scope

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4: report builder (object, columns, filters, grouping) with export and scheduling. The builder must not expose fields
  or records the user may not see; reports are shared and scheduled.

## Decision
- Modules declare datasets (`DatasetDefinition`, fields with type, filter / group / sort flags, optional field permission). Masked or
  secret fields are never declared.
- Execution in the owning module (own DbContext, its scoped query, `DatasetQuery<T>` helper translating the whitelisted definition to
  `IQueryable`). At **every run** fields the runner may not see are dropped from columns and **refused** in filters, grouping and sort
  (400 `reporting.errors.fieldNotAllowed`). Shared and scheduled reports run with the **runner's / recipient's** rights.
- Paging ≤ 200; Excel / PDF export up to a configurable row limit (default 50 000); exports audited.

## Consequences
No generic SQL builder and no cross-schema joins; a report spans one object (with its declared lookups / parent fields).
