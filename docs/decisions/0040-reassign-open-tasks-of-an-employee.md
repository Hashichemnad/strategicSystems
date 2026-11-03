# 0040 — Reassigning the open tasks of an employee who leaves or moves

- **Status:** Accepted (Phase 6, before the pull request — owner request)
- **Context:** Tasks belong to people, not roles, so the bulk role transfer (SPEC §0.2) does not move them. When an employee leaves, their open
  tasks must not stay stuck. The owner asked for an administrative action: choose the leaving employee and either a new assignee or "the
  original creators decide", preview the tasks, reassign with a reason, notify everyone involved, audit it, and offer it when an employee's
  status changes to *Left*.

## Decision
- New permission **`tasks.reassignment.manage`** (scopable). Its data scope applies to the **leaving employee** (owner employee + their unit),
  so a unit-level administrator can act for their unit only; outside the scope the employee answers 404. Seeded in *Task administrator*
  (All), which is now one of the platform administrator roles (as AI and MCP).
- **Preview** (`GET /api/v1/tasks/reassignment/{employeeId}`) lists every open task assigned to the employee, **confidential ones included**
  (they must not be orphaned), but without title or number when the administrator may not read them.
- **To another employee** (`mode = to-employee`), per status:
  draft and submitted keep their status (the creator still edits / approves); assigned / to do are re-assigned; in progress, on hold and
  rejected restart as **Assigned** (Full) / **To Do** (Simple) for the new person, the running timesheet period closed with end event
  `transferred`. Deadlines are recomputed with the new assignee's unit. Tasks the leaver created themself also get the new person as
  **creator** (approver).
- **The creators decide** (`mode = to-creators`): work not yet submitted comes back as **Rejected** with the reason, which is the SPEC's own
  "creator: reassign or cancel" state; submitted and draft tasks stay with the creator. Tasks the leaver created themself have no other
  creator to decide, so a **fallback employee** is required for them (validation error otherwise) and takes them as assignee and creator.
- One transaction: task changes (audited field by field), timeline entries with the reason, record roles (assignee / creator moved), and the
  notifications — one summary each to the new assignee (`tasks.reassignedToYou`), each creator (`tasks.assigneeChanged` or
  `tasks.decisionNeeded`) and the leaver (`tasks.yourTasksReassigned`, skipped when the account is inactive). After the commit one audit
  event `tasks.task.bulk-reassign` on the employee lists the tasks, the mode, the target and the reason. Idempotency key supported.
- **Offer on "Left"**: the Tasks module implements `IEmployeeLifecycleHook`; when an employee becomes *Left* with open tasks, holders of the
  permission (direct user assignments) and the employee's manager get `tasks.employeeLeftWithTasks` with a link to the action. **Nothing moves
  automatically** — who takes the work is a person's decision. The employee page shows the same offer (count + link) to permission holders.
- Not covered: tasks the leaver **created** but assigned to other people (the leaver stays their creator and approver).

## Consequences
Tasks a leaver created for other people keep the leaver as creator: once submitted they wait for an approval nobody can give. A
"take over as creator" action is a follow-up if the owner wants it.
