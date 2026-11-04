# 0061 — KPI result lifecycle, Not Applicable and value units

- **Status:** Accepted by the owner (Phase 12, before the pull request)
- **Context:** SPEC §2.4 K3/K4: submit locks the result, the owner can recall while pending, reject / return unlock it with mandatory
  comments, the Strategy Office reopens a result or a period, the PMO can lock a period at year end; N/A excludes a period from calculations.

## Decision
- Statuses `not_open · open · pending_approval · returned · approved · locked` with an explicit transition table (design note §3).
  Reject and Return both lead to `returned` (the SPEC treats them alike: unlocked, corrected, resubmitted). Reopen needs a reason.
- **Not Applicable is a submission** (with a reason) that goes through the same approval: the reviewer verifies the claim like a value.
  Approved N/A results are excluded from aggregation, scorecards and data-quality evidence completeness.
- Values and targets are numbers **in the KPI's unit** (a percentage KPI stores 92.5 for 92.5 %), as in the K5 test table; performance is a
  ratio (1.0 = 100 %). The unit is on the annual plan, so a target and its values cannot disagree.
- Evidence on submit is required by default (Settings toggle); causality and corrective actions are required when capped performance < 100 %.
