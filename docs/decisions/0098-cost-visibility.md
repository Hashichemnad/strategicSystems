# 0098 — "View costs / budget" for project team members

- **Status:** Accepted by the owner (Phase 10)
- **Context:** SPEC §1.9: costs / budget visible to owner and PM, "Configurable" for team members, not to stakeholders. Phase 9 showed
  costs to everyone who could read the project (known gap).

## Decision
- New scopable permission `portfolio.project.costs` granted by default to every Portfolio role that reads projects and to the record roles
  owner, PM, requester, approver and procurement — **not** to team-member.
- Setting `teamMembersSeeCosts` (default **off**, the safer value) lets the team-member record role see costs.
- A single `CostVisibility` service decides, and every read path uses it: project, milestones, contracts / payments, change requests,
  baselines, EVM (403), status report PDF, dashboards (aggregates only over cost-visible projects), search, Ask / approver brief record
  source, MCP tools and list exports. Hidden values are returned as null with `costsHidden = true`.

## Consequences
A team member sees the project and its plan but no money unless the installation allows it; tests cover each read path.
