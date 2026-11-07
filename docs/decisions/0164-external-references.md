# 0164 — External references: source systems, `integration.external_ref`, uniqueness and origin

- **Status:** Accepted (Phase 19, stream X)
- **Context:** SPEC-ADDENDUM §B: data from other systems (first Salesforce standby sync, Phase 23; then any customer system) must stay unique
  and traceable. A record may carry references from several sources; a source's external ID must map to exactly one record and a record to
  at most one external ID per source and type. Records created by a source show "from {source}".

## Decision
- New platform component **`Strategy.Platform.Integration`** (schema **`integration`**, CLAUDE.md §5) with **`Strategy.Platform.Integration.Contracts`**
  (Kernel only, like `Links.Contracts`). Platform components (Organisation, Calendar) and modules reference only the contracts.
- **Source systems** (`integration.source_system`): stable lower-case `code`, AR / EN name, `type_code` from the editable picklist
  `integration.source_type` (seeded Salesforce, SAP, Oracle, Microsoft Dynamics, Custom — configuration, not an enum), description, active.
  Never deleted: deactivated. Managed with `integration.source-system.manage` (new `integration.administrator` role, MFA, granted to the
  platform administrators like the other administrator roles, ADR 0016); listed with `integration.source-system.read` (role
  `integration.viewer`, assigned to everyone, so record pages can name the source).
- **`integration.external_ref`** (source code, entity type, external ID, record id, `is_origin`, first seen, last synced, source modified,
  source hash, sync status) with two **unique indexes**: (source, type, external ID) and (source, type, record id). The store validates both
  first and returns validation errors with message keys (`integration.errors.externalIdTaken`, `…recordAlreadyReferenced`); a concurrent
  writer that passes the check hits the index and the PostgreSQL unique violation is **mapped to the same error** by index name.
- **Entity types**: the Links registry's linkable types, plus a small registry of **referenceable types** in the contracts
  (`ReferenceableTypeDefinition` + `IReferenceableRecordSource`) for records that are not linkable (organisation units, employees, holidays).
  The resolver (Links registry or the registered source) is the only visibility check: reading references of a record answers 404 unless
  the user may read the record.
- References are written in the caller's transaction (`transactionContext`, ADR 0023). In a joined transaction the insert runs under a
  savepoint, so a unique violation leaves the caller's transaction usable (it receives the validation error and decides).
- **Origin**: `is_origin = true` when the record was created from that source (import row, REST PUT create, sync). The record header shows
  the origin badge from the first origin reference. Phase 23 sets it for synced records and uses `sync_status` (`synced`, `pending`, `error`,
  `deleted_in_source`) and `source_hash` / `source_modified_at` for change detection.
- **Unlink** deletes the mapping row through EF Core (audited by the interceptor as a delete of `integration.external_ref`). The row is a
  mapping, not a business record (CLAUDE.md §6 "no hard deletes" is about business records); keeping deleted rows would need partial unique
  indexes and would block re-linking the same ID.
- `integration.external_ref` and `integration.source_system` are `[Audited]`. The record History tab reads one entity per record, so
  reference changes are visible in the global Audit Log (entity `integration.external_ref`), not in the record's History tab.

## Consequences
Phase 23 only adds connectors and mappings on top of the store; Phase 22 integration clients reuse the REST upsert (ADR 0165). One extra
query per record page (references) and one per report page (external columns).
