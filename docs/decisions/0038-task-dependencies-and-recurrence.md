# 0038 — Task dependencies and recurrence

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §11.5 lists optional dependencies (for project tasks) and an optional recurrence rule without further rules.

## Decision
- **Dependencies** are finish-to-start between tasks the creator can read. Cycles are refused. A dependent task can be accepted and worked
  on, but **cannot be submitted / completed while a predecessor is open** (approved / done / cancelled count as finished); the task page
  shows "waiting for". Only the creator edits dependencies. This is the least restrictive rule that still protects the order of delivery.
- **Recurrence**: frequency daily / weekly / monthly, interval 1–12, end by date or count (≤ 366), single assignee only. The task created
  by the user is occurrence 1 and holds the rule. A daily job creates the next occurrence when its planned start (or due date when there is
  no planned start) is reached — same content, assignee and link, dates shifted by the rule, status Assigned / To Do. Cancelling the first
  task ends the series; occurrences are independent tasks afterwards.

## Consequences
Lead / lag times and other dependency types are not modelled (Gantt dependencies come with Phase 10 if needed).
