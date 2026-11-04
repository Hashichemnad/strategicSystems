# 0120 — CAP data model: sources, remarks, many-to-many actions and numbering

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.3: remarks under remark sources, actions that can serve several remarks ("link an existing action"), remark numbers
  `CAP-{UNITCODE}-{YYYY}-{NNN}` and action numbers `{RemarkNo}-{n}`. CLAUDE.md §6: numbers come from the Numbering service with configurable
  formats.

## Decision
- Schema `cap`: `source`, `remark`, `action`, `remark_action` (many-to-many, PK remark + action), `achievement_update`, `extension_request`,
  `effectiveness_check`, `suggestion` (candidates raised by other systems). Every action has a **home remark** (where it was created);
  further remarks link to it.
- Numbering formats `cap.source` `CAPS-{YYYY}-{SEQ:3}` and `cap.remark` `CAP-{UNIT}-{YYYY}-{SEQ:3}`, where `{UNIT}` is the **concerned
  department's** code (the remark belongs to that department's plan; the SPEC example `CAP-SPMO-…` is a remark of the SPMO department).
- Action numbers `{RemarkNo}-{n}` are derived, not a numbering format: `n` is a counter on the home remark incremented in the creating
  transaction. The format cannot be changed in Settings because its prefix is another record's number; the remark format can.
- Root cause, risk / KPI facts and source-system viewers are jsonb columns; picklists are lookups (codes in logic).

## Consequences
An action linked to two remarks keeps its home number; the second remark lists it with a "linked" chip. Renaming a department's code
changes only new numbers.
