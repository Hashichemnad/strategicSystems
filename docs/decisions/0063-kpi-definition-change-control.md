# 0063 — KPI definition change control

- **Status:** Proposed (Phase 12) — awaiting owner acceptance
- **Context:** SPEC §2.7: changes to target, equation, unit or direction after the year starts require Strategy Office approval; full
  version history.

## Decision
- "The year started" = the card has an annual plan for a year ≤ the current year.
- Changes to those four items then create a `definition_change` request routed through approval process `kpi.definition-change`
  (default: one Strategy Office step, configurable). Approval applies it in the approval transaction: the card, and for a target change
  the current year's plan and the period targets of results not yet approved; a `card_version` snapshot is written.
- All other edits apply directly and also write a version. Feature `kpi.change-control` (on by default) can switch the control off.
