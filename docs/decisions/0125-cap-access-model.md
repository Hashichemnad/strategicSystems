# 0125 — CAP access: PM Office role, record roles, unit scope

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.7: CAP Administrator (PM Office) all; remark owner on assigned remarks; assigned employee on assigned actions;
  department manager unit and below; source-system roles on linked records; leadership all (dashboards). CLAUDE.md §7.

## Decision
- System `cap`; roles `cap-office` (All, MFA, approval role key `cap.cap-office`), `department-manager` (unit and below, read / dashboard /
  export), `participant` (everyone, Own), `viewer` (All read, dashboards). Remark scope: own = owner employee, unit = department unit.
- Record roles on `cap.remark` granted by the module: `remark-owner`, `action-assignee` (read + action update; handlers also check the
  assignee), `source-viewer` (read — e.g. Benchmarking coordinator and topic owners), `approver` on `cap.action` while a task is open.
- Actions are readable through any readable remark or as their assignee. 404 outside scope, 403 for visible-but-forbidden.

## Consequences
"Department manager" is a role assigned to units (heads of assigned units get it), not a hard-coded hierarchy rule.
