# 0131 — Audit data model, numbering and closure rules

- **Status:** Proposed (Phase 16)
- **Context:** SPEC §10.3 A / B: reports, findings (AR-NNNN), recommendations (AA-NNNN), progress updates (AAQ-NNNN), revision and
  risk-acceptance log; statuses partly computed.

## Decision
- Schema `audit_mgmt`, system `auditmgmt` (the platform audit trail owns `audit`). Numbering formats `AUD-{YYYY}-{SEQ:3}`, `AR-{SEQ:4}`,
  `AA-{SEQ:4}`, `AAQ-{SEQ:4}`, `ENG-{YYYY}-{SEQ:3}` — editable in Settings → Numbering.
- Recommendation status is derived (not started / continuing / late / completed) unless final (completed, risk accepted, cancelled).
  A finding closes when all its recommendations are Completed / Risk Accepted / Cancelled; when every non-cancelled one is risk accepted
  the finding is **Risk Accepted** (reported separately), otherwise **Closed**; a finding without recommendations stays open. A report
  closes when every finding is closed / risk accepted / cancelled (at least one not cancelled). Pure functions, table-tested.
- Each recommendation has its own **required** due date (no default, SPEC §10.8); revisions keep the original and log the reason.
- No hard deletes: cancel with reason (kept in history, excluded from follow-up and closure).

## Consequences
A finding cannot be closed by hand; closure always follows its recommendations, so the department view and dashboards agree.
