# 0046 — Team lifecycle

- **Status:** Accepted (Phase 7)
- **Context:** SPEC §4.4 A lists statuses Draft · Pending Approval · Active · Inactive · Pending Evaluation · Evaluation Waiting Approval ·
  Closed · Replaced, and §4.5 the formation, evaluation, close / replace flows.

## Decision
- The transition table in `TeamLifecycle` (docs/design/teams.md §4) is code with unit tests for every allowed and forbidden pair.
- **Cancelled** is added for requests that never became a team (withdrawn, cancelled draft, formation rejected) — CLAUDE.md §6 "no hard
  deletes, use Cancelled with reason". A **returned** formation goes back to Draft for correction and resubmission.
- Types without formation approval activate on submit.
- After an approved evaluation a permanent team (or a temporary team before its end) returns to **Active**; a temporary team whose end
  date passed, whose closure was requested or whose linked record closed becomes **Closed** (SPEC T5 "after evaluation").
- Closing a temporary team of a type that evaluates on end / closure opens the completion evaluation first (Pending Evaluation) instead of
  closing at once; types with trigger `none` close directly.
- **Replaced**: a formation request may name the team it replaces; its approval marks the old team Replaced and links both ways.
