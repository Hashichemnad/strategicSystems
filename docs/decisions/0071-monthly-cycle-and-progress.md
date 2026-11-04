# 0071 — Monthly update cycle, achievement lock and progress formulas

- **Status:** Proposed (Phase 9)
- **Context:** SPEC W4: on the 1st of every month the update window opens and the PM is emailed; the PM updates milestones (actual start, Achieve
  %, actual end + evidence at 100 %, causality + corrective actions when delayed) and submits; approval (Q-P1: Department Director → PM Office);
  approved achievement is locked. Q-P6: Progress Status = Delayed when approved progress is more than 5 points (configurable) below
  planned-to-date or any milestone is past its planned end below 100 %. All deadlines use the shared business-hours calculator (ADR 0027).

## Decision
- One `monthly_update` row per project and period (`YYYY-MM`), created by the hourly job `portfolio.monthly-cycle` for active projects in
  Execution. **Opening moment:** the first working moment on or after the 1st in the project unit's business hours (a non-working 1st rolls to
  the next working day). **Due:** end of working time *N* working days after opening (`monthlyDueWorkingDays`, default 10). Reminder *R*
  working days before due (default 3), one overdue notice. Stamps make every step happen once (idempotent re-runs). A period not yet approved
  blocks the next window (no pile-up). The PM Office can open the window now (audited); once this month's window is approved it may open an
  **additional window in the same month** (`sequence` 2, 3…) — e.g. to record a milestone finished early instead of waiting for the 1st.
- Milestone updates are **pending values** on the milestone (`pending_achieve`, pending actual dates, causality, corrective actions, evidence
  files) while the period is open / returned. Rules (server-side): 0 ≤ achieve ≤ weight; **achieve ≥ Max Achieved** (approved achievement is
  locked, never lowered); first achievement needs an actual start; achieve = weight needs actual end **and** at least one evidence file;
  delayed (today after planned end, achieve < weight) needs causality and corrective actions. Open linked tasks at 100 % give a warning only.
- Submitting starts `portfolio.monthly-update`; approval copies pending values to approved (Max Achieved, actual dates, status completed at
  100 %), snapshots the update, recalculates progress, and moves the project to Closure (or initiative completion) at 100 %. Return / reject
  reopen the period with the pending values kept.
- **Planned-to-date** is linear within each leaf milestone's planned dates (0 before start, full weight from the end date) — finer than "sum of
  weights already due" and identical at month ends for short milestones. Tolerance default 0.05 (5 points), Settings → Portfolio.

## Consequences
Vendor-then-PM mode (Wasel, Phase 11) will feed the same pending values. Progress is recalculated on every approval and nightly-safe on read.
