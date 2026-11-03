# 0049 — Automatic Project / Initiative creation and `PROJ-…` numbering

- **Status:** Proposed (Phase 8)
- **Context:** SPEC W1.7: final approval auto-creates the Project / Initiative (Stage = Initiation for projects) and copies the request
  data; the ID format is `PROJ-SPMO-YYYY-NNN` where `SPMO` is the ADoF strategy / PM office. Nothing organisation-specific may be coded.
  The project lifecycle itself is Phase 9.

## Decision
- The approval hook (inside the engine's transaction, ADR 0023) creates the `portfolio.project` row when the instance is approved: number,
  classification (effective, i.e. the PM Office override if any), name, description, scope, unit, section, objective, program type, budget
  (approved budget), planned Year 1–4 costs, planned start / end, requester, **owner = department director of the request's unit** at that
  moment (SPEC §1.9 Project Owner default), stage `initiation` (project) or `planning` (initiative, Q-P2), status `active`. The request
  stores `project_id`; record roles `owner` and `requester` are granted on the project; the requester is notified.
- Numbering key `portfolio.project`, default pattern **`PROJ-{UNIT}-{YYYY}-{SEQ:3}`**: `{UNIT}` is the code of the **owning unit of the
  Portfolio system** (Settings → Systems), falling back to the request unit's code; `{YYYY}` is the approval year. With the PM office as
  owning unit this reproduces `PROJ-SPMO-2026-001`; the pattern is editable in Settings → Numbering.
- Exactly one project per request (unique index on `project.request_id`); a repeated hook run finds it and does nothing.

## Consequences
Phase 9 extends the project table and its state machine; Phase 8 shows a minimal project page (stage stepper, "waiting to take ownership").
