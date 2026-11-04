# 0102 — Risk lifecycle, one pending process, approvals R1 / R3 / R5

- **Status:** Accepted by the owner (Phase 13, before the pull request)
- **Context:** SPEC §3.4 A statuses Draft · Under Review · Active · Closed · Rejected; §3.5 R1 review, R3 periodic review, R5 closure
  (default Risk Office; Critical also Risk Committee); all approvals configurable.

## Decision
- Explicit `RiskLifecycle` table (draft, under_review, active, closed, rejected). Return for correction goes back to `draft` with a
  `returned` flag and comment. Closure and periodic review are **pending processes** on an active risk (`pending_process`), one at a time.
- Processes `risk.review`, `risk.periodic-review`, `risk.closure` with default step role `risk.risk-office`; `risk.closure` has a second
  step role `risk.risk-committee` with the entry condition `residualBand == "critical"`. Variables: `inherentBand`, `residualBand`,
  `residualScore`, `withinAppetite`, `top10`.
- The number is assigned on the first save of the draft (SPEC R1.2) — a discarded draft keeps its number consumed (numbers are never
  reused). Drafts never submitted may be discarded (audited hard delete, CLAUDE.md §6).
- Reopen (closed → active, rejected → draft) is a Risk Office action with a reason, snapshotted.

## Consequences
The Risk Office adjusts ratings / response / Top-10 during review through its own edit (audited) and then approves on the generic panel.
