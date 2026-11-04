# 0091 — Agile progress formula and the sprint / monthly roll-up approval modes

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.13: agile progress = Σ (epic weight × % story points done); hybrid = weighted mix; "Agile progress approval
  (decided): configurable per installation, overridable per project — approval after every sprint, or one monthly roll-up of all sprints in
  that month, following the same approval chain and rhythm as waterfall monthly updates". The default is not stated.

## Decision
- Progress counts **approved** completion only (as waterfall counts approved achievement): epic fraction = points of approved-done active
  stories ÷ points of active stories of the epic; progress = Σ weight × fraction, capped at 100 %. The unapproved figure is shown as "pending".
- **Default mode `monthly`** (same rhythm and chain as waterfall; fewer approvals); a project may override (`sprint` / `monthly`).
  Sprint mode uses the new process `portfolio.sprint-review` (seeded Department Director → PM Office, editable); the monthly job opens no
  window for such projects. Monthly mode: the monthly submission carries every closed, unapproved sprint; its approval approves them.
- Hybrid is always monthly: a milestone marked *delivered in sprints* gets its pending achievement from its epics (weight × Σ epic weight ×
  done fraction) at submission; the PM cannot type it. Project progress then follows the waterfall formula.
- Approval locks the counted stories (`approved_done`), like the waterfall achievement lock (ADR 0071).
- Agile planned-to-date is linear between the project's planned start and end; the Q-P6 status rule applies unchanged.

## Consequences
One progress % for every method feeds the dashboards, RAG and EVM. A returned sprint goes back to *closed* for corrections.
