# 0045 — Suggested criterion levels and minutes delay

- **Status:** Accepted (Phase 7)
- **Context:** SPEC §4.1 / §4.5: the "recommendations closure" and "governance / minutes timeliness" criteria are suggested from data; the
  leader confirms or overrides with justification. Policy text: minutes within 24 h = 10 · 48–72 h = 5 · poor = 0 (24–48 h undefined).

## Decision
- A criterion may declare `dataSource`: `recommendations-closure` (percent) or `minutes-delay` (hours) and numeric ranges per level. The
  suggestion is the level whose range holds the value; no data → no suggestion.
- **Closure %** = closed ÷ (all − rejected) over recommendations (kind `recommendation`, decisions excluded) created in the cycle period.
- **Minutes delay** = elapsed hours between the meeting's end and "Submit minutes" (calendar hours, as the policy speaks of 24 h; not
  working hours). Meetings held in the period without minutes count with the delay up to now. The criterion uses the **average**.
- Default ranges: closure ≥ 95 → 25, 80 to < 95 → 12.5, < 80 → 0; delay ≤ 24 → 10, > 24 to ≤ 72 → 5, > 72 → 0 — the 24–48 h gap
  goes to the middle level (benefit to the team), editable per template.
- Choosing another level than the suggestion requires an override justification (validation), shown to HR.
- Cycle period: annual cycles = the calendar year ∩ team life; completion cycles = team start → closure / end date.
