# 0216 — Picklist values ship in the mapping pack

- **Status:** Proposed (release 1.0.2) — awaiting owner acceptance
- **Context:** About 150 of the new fields (ADR 0215) are Salesforce picklists. Their values are ADoF's, for example audit classifications,
  fraud categories and task sources. CLAUDE.md §2.2 keeps organisation-specific values out of code. The pack (ADR 0203) is already the place
  for ADoF's Salesforce specifics.

## Decision
- New lookup types are declared in code **without values**.
- The pack format `strategy.sync-pack/1` gains an optional `lookups: [{ type, values: [{ code, labelAr, labelEn }] }]` section.
  `ImportPackAsync` and the shipped-pack upgrade (ADR 0217) add, through `ILookupProvisioning` (Settings), the codes a type does not have yet.
  They are added active, after the existing values, audited by the interceptor.
- Existing values are **never changed**: an administrator's renamed, re-sorted or deactivated values stay as they are.
- A lookup type unknown to the installation makes the import fail with `integration.sync.errors.unknownLookupType`.
- **Codes** come from `code_of` in `tools/salesforce/export_snapshot.py`: lower-case ASCII slugs of the Salesforce value, or `v-` plus 10 hex
  digits of its SHA-1 for values without Latin letters (Arabic-only). Labels come from the export (Arabic translation and English label).
- Picklists that drive fixed logic (stages, statuses, directions) keep their fixed codes and map through value maps, as before.

## Consequences
- Other customers get the lookup types empty and fill them in Settings, or import their own pack.
- New Salesforce values appear as unmapped in *Validate* and as `unmappedValue` exceptions. *Map value* fixes both.
