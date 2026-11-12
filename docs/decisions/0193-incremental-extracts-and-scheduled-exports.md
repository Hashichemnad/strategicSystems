# 0193 — Incremental extracts, the deleted feed and scheduled folder exports

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** ETL tools need "changed since" extracts with a deleted-records feed (JSON / CSV) and optionally scheduled exports to a
  folder / SFTP / S3. Business records are never hard-deleted (CLAUDE.md §6): "deleted" means cancelled / withdrawn / inactive.

## Decision
- `GET /api/v1/integration/extracts/{system}/{set}` (changed: `modifiedAt > changedSince`) and `…/deleted` (records matching the type's
  declared deleted condition, changed since), keyset-paged by (`modifiedAt`, `id`) with an opaque cursor, `watermark` for the next run,
  JSON or CSV (UTF-8 BOM, code + label columns). Same permission filtering and audit as OData. Hard-deleted drafts are not listed (they were
  never visible outside their author's drafts).
- Scheduled exports: per client, record types, format, frequency; target = a sub-folder of the operator-set `Integration:ExportRoot` only
  (no administrator-typed absolute paths, traversal refused); runs as the client with its watermark. **SFTP and S3 targets are deferred**:
  they need credentials management and an SSH library; operators sync the folder or pull the REST extract.

## Consequences
ETL loads are idempotent (upsert by `id`, delete by the deleted feed). The folder export covers on-premises file drops.
