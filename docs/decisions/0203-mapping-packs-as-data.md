# 0203 — Mapping packs as data, validated against the live describe

- **Status:** Proposed (Phase 23) — awaiting owner acceptance
- **Context:** §G2: mappings per Salesforce object → internal record type (direct fields, picklist → lookup code, lookup → external reference,
  user → employee, transformations, parent-child order, record types, defaults, ignore list) are **data**, editable with an editor that reads the
  live describe, shipped as an exportable "ADoF Salesforce" pack. Nothing organisation-specific in code (CLAUDE.md §2.2).

## Decision
- Tables `sync_mapping_pack` / `sync_object_mapping`; field mappings, value maps, defaults and ignore list are jsonb validated by the server
  (field kinds `direct`, `picklist`, `recordType`, `reference`, `person`, `constant`; transforms `trim`, `upper`, `lower`, `percentToRatio`,
  `ratio`, `dateOnly`, `htmlToText`, `truncate:n`, `yesNo`). An object may be mapped twice (e.g. team row → team and → yearly evaluation) with an
  `external_id_template` (`{Id}`, `{year:Field}`).
- **Validation against the live describe** before an object can be enabled (`confirmed_at`); any edit clears it. New picklist values in
  Salesforce show up as unmapped in the validator and, at run time, as `unmappedValue` exceptions.
- The ADoF pack is an embedded JSON resource seeded once (inactive); export / import uses the same format (`strategy.sync-pack/1`, no secrets,
  no watermarks). Objects not visible to the integration user today ship **disabled and unconfirmed** with provisional names from the 1.0
  source documents, documented as such.

## Consequences
ADoF-specific values (picklist texts, object names) live only in the pack, which a customer edits or replaces. Pack import is audited.
