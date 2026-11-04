# 0072 — Team formation through Teams; project access from team membership; closure events

- **Status:** Proposed (Phase 9)
- **Context:** SPEC W2.5: after the LPO, the PM Office forms the project team through Teams Management (formation request → approval → memo →
  team Active → Stage = Planning). §1.9: team members' access "follows team membership automatically". ADR 0043: closure approval publishes
  `LinkedRecordClosed` so Teams opens the project-team evaluation. Modules talk only through Contracts and integration events.

## Decision
- `portfolio.project` is declared in the Links registry as **task-able and team-able**. The PM Office uses the shared *Request a team* flow on the
  project's Team tab; the team links itself to the project (Teams owns the link).
- **Teams publishes `TeamMembershipChanged`** (new, in `Strategy.Modules.Teams.Contracts`, additive) from its single member-role sync point
  whenever a *formed* team's members or status change: team id, status, active member employee ids, leader. Delivered through the outbox in
  the Teams transaction.
- Portfolio subscribes: for every project the team is linked to (Links registry, team-able), it (1) stores the team on the project and, when
  the project waits in `team-formation` and the team is **Active**, moves it to Planning and notifies the PM; (2) **syncs the project record
  role `team-member`** to exactly the team's active members (grants and revokes), so project-team members read the project and contribute
  (challenges, meetings, lessons) without any manual grant. Closed / cancelled teams drop the role.
- Closure approval publishes, in the approval transaction, `LinkedRecordClosed(portfolio.project, id, "closure-approved")` (Teams opens the
  completion evaluation of the linked project teams) and `ProjectClosureApproved` (Portfolio.Contracts) for other subscribers. Cancellation
  publishes `LinkedRecordClosed(…, "cancelled")`.

## Consequences
Teams gains one event and no Portfolio knowledge. The handler is idempotent (role sync is a set comparison; the stage move checks the sub-state).
