# 0076 — Phase 9 AI and MCP scope

- **Status:** Proposed (Phase 9)
- **Context:** SPEC §1.15 lists the charter assistant, plan generator and update assistant; BUILD_PLAN puts "remaining AI assists" in Phase 10.
  MCP: read tools on, write tools off, no approve tool (ADR 0031).

## Decision
- **No new AI features in Phase 9.** The charter assistant, plan generator and update assistant arrive with Phase 10 (with the status report
  writer and change impact). The existing record source and search source cover projects (Ask / approver brief now include stage, PM,
  progress and status).
- MCP tools: `portfolio_get_project` (extended: stage, PM, progress, approval status), `portfolio_my_projects` (read, on),
  `portfolio_project_milestones` (read, on), `portfolio_update_milestone_draft` (**write, off by default**; saves pending values only while a
  window is open — never submits).

## Consequences
Phase 9 stays within its build-plan scope; the AI assists build on the Phase 9 data model in Phase 10.
