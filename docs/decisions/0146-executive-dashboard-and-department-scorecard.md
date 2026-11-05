# 0146 — Executive dashboard and department comparison scorecard

- **Status:** Accepted by the owner (Phase 17)
- **Context:** SPEC §0.4 level 4 lists the sections but not the department level, the scorecard columns' thresholds or who may open it.

## Decision
- Permission `reporting.executive.read`, new role *Executive viewer* (MFA), not assigned to everyone. Sections appear only for systems
  the viewer can access; figures are scoped (an Executive viewer with unit scope sees their units).
- Department = org level from Settings → Reporting `departmentLevelCode` (default `department`); lower units roll up.
- Scorecard columns: KPI score (capped, weighted), project health (% green / red count), open high and critical risks, outside appetite,
  open / late audit findings, CAP closure rate, overdue tasks. Cell RAG thresholds per column in Settings (seeded).
- Drill path: figure → system dashboard filtered (period, unit) → record list with the same filters → record; targets re-check access.
- One request returns the whole page from facts; lists come from scoped module list sources (≤ 10 rows each).

## Consequences
Department rows exist only for units the viewer can see; leadership with scope All sees the whole organisation.
