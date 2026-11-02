# 0025 — Approver rule semantics, self-approval and steps without an approver

- **Status:** Accepted (Phase 4)
- **Context:** SPEC §0.1 / §0.2 name approver types (role, org relation, specific person, team role) and the org relations "Section
  Manager of the requester", "Department Director of the project's department", "head of unit X"; CLAUDE.md §9 adds owning unit of system,
  record role and submitter's manager. The SPEC does not say what "section" / "department" mean in a configurable hierarchy, whether a
  submitter may approve their own request, nor what happens when nobody resolves.

## Decision
- **Section / department** = the nearest unit on the path (the unit itself or an ancestor) whose level code equals the configured
  section / department level (Settings → Approvals; defaults `section`, `department`, the seeded level codes). "Of" = the record's unit
  (default) or the submitter's unit.
- **Role** = users with a current assignment of the role: directly, or through a unit assignment (employees of that unit and below — the
  same meaning access uses). "Everyone" assignments are ignored for approvals (they would make every user an approver).
- **Owning unit of system** = the head of the owning unit set in Settings → Systems.
- **Team role** resolves through `ITeamRoleResolver`; until the Teams module (Phase 7) registers one, the rule resolves to nobody and the
  preview says so.
- **Self-approval:** the submitter is removed from the approvers by default (per step switch "exclude the submitter").
- **Nobody resolves:** per step, `block` (default, safest: the submission / transition fails with `workflow.errors.noApprover` naming the
  step, so nothing is ever approved by nobody) or `skip` (recorded in history). An entry condition that fails at runtime **includes** the step.
- Approvers are resolved when the step is entered and snapshotted on the task; later org changes do not move open tasks, except a bulk
  role transfer (SPEC §0.2) and an SLA escalation.
- Several approvers on one step: any one of them decides ("first response wins"); a row version stops two simultaneous decisions.

## Consequences
Unanimous / quorum steps are not offered; they can be added as a step option later without changing stored data.
