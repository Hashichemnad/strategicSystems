# 0035 — Task approval by the creator is a task-level decision, not an approval-engine process

- **Status:** Accepted (Phase 6)
- **Context:** SPEC §11.3 / §11.12: the approver of a task is always its creator. The approval engine (Phase 4) is built for configurable
  chains of approvers resolved from rules, with SLAs, delegation and the Approval Settings centre.

## Decision
- Approve / return / reassign of a submitted task are **transitions of the task's own state machine**, allowed only to the creator (actor
  check on the task) holding `tasks.task.manage` through the **creator record role**. No process key is registered and nothing appears in
  Approval Settings: there is nothing an administrator could configure without breaking §11.12.
- The decision, feedback point and comment are recorded in the task timeline (`tasks.task_event`) and in the audit trail (field changes and
  the `tasks.task.approve` event).
- Permission **and** actor are both required, so granting `tasks.task.manage` to a system role does not let anyone else approve.
- Delegation "on behalf of" (approval engine) does not apply to tasks; a creator on leave can reassign or the secretary can follow up.

## Consequences
Pending task approvals are not in the workflow inbox; My Work shows them through the Tasks card ("created by me, awaiting my approval").
If an organisation later wants multi-step task approval, a task type could start an engine process on submit (not built).
