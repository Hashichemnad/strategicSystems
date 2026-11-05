# 0133 — Follow-ups, late status, reminders and the optional review step

- **Status:** Proposed (Phase 16)
- **Context:** SPEC §10.3 B steps 2–5: the Audit Office records each follow-up; reminders before each next follow-up date and before
  due dates; late when past the due date below 100 %; optional internal review (auditor → CAE), off by default.

## Decision
- A follow-up requires the next follow-up date when below 100 % (after the follow-up date); 100 % completes the recommendation.
- Late = effective due date (revised ?? original) before today (installation time zone, `IClock`), recomputed on every change and hourly by
  job `auditmgmt.monitor`. Reminders go to the responsible auditor (finding → report lead auditor) N days before (working days of the
  responsible unit's business hours by default), each once per date (stamps).
- Setting `followUpReview` (off): when on, a follow-up is `pending` and applied only after approval process `auditmgmt.follow-up-review`
  (default step: role `auditmgmt.chief-audit-executive`); returned follow-ups are not applied.
- Optional notices to department users (`notifyDepartments`, off) when a finding concerning them is created, updated or closed.

## Consequences
The follow-up calendar (week / month, by auditor) reads the same dates the reminders use.
