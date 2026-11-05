# 0130 — Record roles granted to a unit (audit department scope)

- **Status:** Accepted by the owner (Phase 16)
- **Context:** SPEC §10.6: department users see the findings of their own section / department — configurable "section only, or department
  and below". The data scopes (ADR 0015) start from the user's own unit (and the units they head): staff of a section can never reach a
  finding addressed to their department or to a sibling section. CLAUDE.md §7 asks for one access service; a module-side widening would
  bypass it.

## Decision
- `IRecordRoleService` accepts a third principal kind `RecordPrincipalKinds.Unit`. A grant to unit U matches every internal user whose
  employee is placed in U or in a unit below it, or who heads U — the same meaning as a role assignment to a unit. `IAccessService.CanAsync`
  and `ApplyScopeAsync` include such grants (the user's placement path and headed units are part of the cached access snapshot).
- Audit Management (Phase 16) seeds role `department-user` (everyone) with `auditmgmt.finding.read` at **unit and below**; with setting
  `departmentScope = department` it grants record role `department-reader` on each finding to the finding's department unit (nearest
  ancestor at the configured level). Changing the setting re-syncs the grants.
- The approval engine and "effective access" report ignore / list unit grants explicitly; vendor principals never match them.

## Consequences
Generic and additive: existing grants (employee / user) behave as before. The cost is one extra list (ancestor + headed units) per
access snapshot.
