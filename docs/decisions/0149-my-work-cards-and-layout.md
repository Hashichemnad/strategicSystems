# 0149 — My Work cards from every system and the per-user layout

- **Status:** Proposed (Phase 17)
- **Context:** SPEC §0.4 level 1 lists the cards; only cards for accessible systems are shown; Phase 4 delivered approvals, action items
  and submissions through `IMyWorkSource` (items, no per-system cards). There is no per-user preference store beyond language and theme.

## Decision
- New contract `IMyWorkCardSource` (code, title key, counts with tone, ≤ 5 items, link); one per module plus Workflow (approvals),
  Notifications and Reporting (pins). The aggregator calls only enabled systems the user may open, in parallel with a timeout; a failure
  shows an "unavailable" card.
- Layout (order, hidden) per user in `reporting.my_work_layout`; reorder by drag or keyboard; inaccessible cards never shown whatever the
  layout says. `GET /workflow/my-work` stays unchanged.

## Consequences
Every module adds a small card source; the home page becomes configurable without touching modules.
