# 0114 — Plan proposals, vendor update submissions, PM review with diff, monthly approval

- **Status:** Accepted by the owner (Phase 11)
- **Context:** SPEC §1.18 review flow and W3.2 / W4.2.

## Decision
- **Plan proposal** draft → submitted (lead) → accepted / returned (PM) → draft …; withdraw by the vendor. Accepting creates milestones
  (assigned to the vendor) or epics / stories through the plan services; the PM's edits are stored as a diff the vendor sees. Proposed tasks
  are kept on the accepted proposal and created by the PM in the Tasks panel (no cross-module task-creation contract exists yet).
- **Submission** per vendor and monthly window: draft → submitted (lead; same milestone rules as the PM incl. evidence at 100 % and causality
  when delayed) → per item accept / edit-then-accept / return → `accepted` or `returned` → `included` when the monthly update is approved.
- Accepted values become the milestone's pending update with `pending_source` = `vendor` / `vendor-edited`; the monthly approval snapshot
  carries the source and the vendor's original values, shown to approvers. The PM can still update any milestone directly (source `pm`), so a
  silent vendor never blocks the month. Monthly submit is refused while a vendor submission is waiting for review.
- The vendor sees outcomes without internal comments; the PM may share a note explicitly.

## Consequences
The normal `portfolio.monthly-update` approval chain is unchanged; vendor origin is visible end-to-end.
