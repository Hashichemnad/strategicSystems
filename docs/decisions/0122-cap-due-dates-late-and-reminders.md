# 0122 — CAP due dates by priority in working time, late actions, escalation and monthly reminders

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.3 B / §9.9: default due dates by priority — Critical 30, High 60, Medium 90, Low 180 days (editable); priority drives
  reminders and escalation. ADR 0027: every deadline uses the shared business-hours calculator.

## Decision
- Remark due date = assigned date + `dueDays[priority]` **calendar days**, rolled forward to the next **working day of the concerned unit's
  business hours** (holidays included), due at the end of working time that day (`due_at`). Setting `countWorkingDays` counts working days
  instead. Same approach as risk review dates (ADR 0100).
- `cap.monitor` (hourly): an approved action past `revised_end ?? planned_end` below 100 % becomes `late`; the assignee and remark owner
  are notified once, the PM Office after `escalatePmoWorkingDays` (5) working days. A remark past its due date and not completed notifies
  its owner and the PM Office once.
- Monthly reminders on `monthlyReminderDay` (1): one notification per assignee with the counts of their actions by status (Not Started,
  In Progress, Late), stamped per action and month (idempotent).

## Consequences
Due dates never fall on a weekend or holiday of the unit; the job can run any number of times.
