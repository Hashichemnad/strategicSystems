# 0180 — Reportable metadata contract: keys, relationships, join-only datasets

- **Status:** Accepted (Phase 21)
- **Context:** SPEC-ADDENDUM E1 needs reports across record types, incl. across systems, with permissions at every level and no
  cross-schema SQL. Phase 22 (OData `$expand`) needs the same relationships. Phase 17 datasets (ADR 0144) describe one record type each.

## Decision
- Additive to `Strategy.Platform.Reporting.Contracts`: `DatasetFieldType.Reference` (record id, join key, never a column for people) and
  `RichText` (plain-text projection everywhere); `DatasetDefinition.KeyField` and `JoinOnly`; `DatasetRelationship(Code, ParentDataset,
  ParentField, ChildDataset, ChildField, LabelAr, LabelEn)`; `IReportingDatasetSource.Relationships` (default: none); `IReportableMetadata`
  (datasets, relationships, `RunAsync` as a user) implemented by the registry.
- The module that **owns the reference column** declares the relationship, across systems by dataset code only (no project reference).
  A relationship whose other end is not registered is ignored.
- A child is always read through its module's scoped query with `childField in (parent keys)`; join-only datasets (approval history) can be
  read only that way, restricted by the engine to keys of parent rows the caller already sees.

## Consequences
One metadata source for report types, dossiers and OData. Modules expose a few extra `Reference` fields; existing datasets keep their codes
and fields. Phase 22 contract: entity sets = non-join-only datasets, navigation = `RelationshipsFrom`, key = `KeyField`.
