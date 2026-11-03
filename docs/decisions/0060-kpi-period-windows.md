# 0060 — KPI period windows, reminders and the late flag

- **Status:** Proposed (Phase 12) — awaiting owner acceptance
- **Context:** SPEC §2.4 K2 and Appendix A Q-K2: windows open the day after the period ends and are due on the 15th day; H2 / Full Year
  open 1 January of the following year and are due 31 January. Late submission is allowed and flagged. Q4 and Two Year are not named.

## Decision
- Settings → KPI holds one rule per period code `{opensDaysAfterEnd, dueDaysAfterOpen}`. Seeded: every period opens 1 day after it ends;
  Q1–Q3 and H1 are due 14 days after opening (the 15th); **Q4, H2, Full Year and Two Year** (all ending in December) are due 30 days after
  opening (31 January) — Q4 follows the year-end periods because it is reported in the same January cycle.
- The new-year run writes a `period_window` row per (year, period) from the rules; the Strategy Office may edit each year's dates (results
  not yet submitted follow) and lock a period at year end.
- `due_at` = end of working time on the due date in the responsible unit's business hours (ADR 0027); a non-working due date rolls forward.
- Late = submitted after `due_at`; `days_late` = calendar days after the due date. Late results are accepted and count against the
  department's on-time rate.
- Hourly job `kpi.period-windows`: opens windows (confirmed plans), notifies owners once, reminds N working days before due (3), overdue
  notice once, escalation to the head of the responsible unit M working days after due (2). Idempotent through stamps.

## Consequences
Every date is configuration; a customer with other windows edits the rules or the year's dates without code.
