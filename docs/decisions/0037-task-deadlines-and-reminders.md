# 0037 — Task due dates, reminders and escalation in working time

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §11.8 (reminder before due, overdue notice, escalation after N days overdue, accept reminder after N hours, digest);
  ADR 0027 asks every deadline to use the shared `IBusinessHoursCalculator`.

## Decision
- Users choose a due **date**. `due_at` is the end of working time on that date in the business hours of the **assignee's unit**; a
  non-working date (weekend / holiday) rolls to the end of the next working day (the deadline is never in closed time).
- From the settings at the time the due date is set (snapshot, as approval SLAs): `due_soon_at` = start of working time N working days
  before the due date; `escalate_at` = end of working time M working days after it; `accept_remind_at` = assignment + H working hours.
  Changing the due date recomputes them; changing settings affects tasks whose dates are set afterwards.
- "Working day" = a day with working time in that business-hours record (holidays included).
- One job every 15 minutes sends each reminder once (stamps `reminder_sent_at`, `overdue_notified_at`, `escalated_at`,
  `accept_reminded_at`); a daily digest job sends open-task summaries when switched on.
- Escalation goes to the assignee's manager (organisation placement); without a manager, to the creator.

## Consequences
Overdue lists are a plain `due_at < now` query. A business calendar per person (leave) is not modelled.
