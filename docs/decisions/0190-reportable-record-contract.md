# 0190 — Reportable record contract (shared with Reporting 2.0)

- **Status:** Proposed (Phase 22) — awaiting owner acceptance
- **Context:** SPEC-ADDENDUM §F asks for OData `$expand` "along the report-type relationships", and §E1 (Phase 21, built in a parallel session)
  asks each module to expose reportable record types, fields and relationships through Contracts. Phase 21 was not merged when Phase 22
  started. The Phase 17 datasets (ADR 0144) are flat, have no stable row key for child objects (milestones use the project id), no
  modification time, no "deleted" notion and no relationships, and their filter model is AND-only.

## Decision
- A minimal contract in **one new file** `Strategy.Platform.Reporting.Contracts/Records.cs` (namespace `Strategy.Platform.Reporting`):
  `ReportableRecordType` (code, system, entity set, AR / EN labels, read permission, fields, relationships, link), `RecordFieldDefinition`
  (code, labels, `RecordFieldType`, optional field permission, lookup type, fixed choices), `RecordRelationship` (name, target type,
  source field, target field, many), `RecordQuery` (fields, neutral filter tree `RecordFilter` — And / Or / Not / Compare / Text / In —,
  sort, skip / take, total, `ChangedSince`, keyset `After`, mode `Current | Deleted`), `RecordPage`, and `IReportableRecordSource`.
- Every type has the implicit fields **`id`** (key) and **`modifiedAt`** (`AuditableEntity.UpdatedAt`). Each type declares its
  **deleted** condition (cancelled / withdrawn / inactive) for the deleted feed.
- Modules implement it in a `Feeds/` folder with the helper `Strategy.Platform.Reporting.Records.RecordTypeQuery<T>` on the same scoped
  reader as their screens; the consumer (OData, extracts) validates fields against the caller's permissions first. Relationships join in
  the consumer (one query per relationship with an `In` filter) — no cross-schema SQL.
- Relationships are declared within a system; cross-system relationships (§E1) are left to Phase 21's report types.

## Consequences
Phase 21 can adopt this contract for report types (fields, relationships, joins in the reporting layer), or map its own contract onto it
when the branches meet; the names do not collide with Phase 17's dataset types, so both branches compile side by side. Module datasets
(ADR 0144) stay as they are.

## After Phase 21 merged (ADR 0180)
Phase 21 merged its own reportable metadata contract: the Phase 17 datasets extended with `KeyField`, `JoinOnly` and `DatasetRelationship`
(including cross-system relationships). Both contracts now exist side by side and compile without conflict: report types and dossiers use
ADR 0180, OData feeds and extracts use this one (which adds what feeds need and datasets lack: `modifiedAt`, the deleted condition, keyset
paging and an OR / NOT filter tree). **Follow-up for owner decision:** converge on one contract — either extend ADR 0180 datasets with those
four capabilities and move the feeds onto them (cross-system `$expand` would come for free), or keep both. Until then a module declares its
fields twice (dataset + record type).
