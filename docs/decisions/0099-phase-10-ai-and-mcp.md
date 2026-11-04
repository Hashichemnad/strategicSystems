# 0099 — Phase 10 AI assists and MCP tools

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.15 B; CLAUDE.md rule 6 (AI drafts, people decide); ADR 0031 (read tools on, write tools off, no approve tool).

## Decision
- Features (each with its AI switch and the permission `portfolio.ai.assist`): charter assistant, plan generator, update assistant, status
  report writer, change impact analysis, portfolio insights. Each returns validated JSON that is **never saved by the AI**; the user picks
  and saves through the normal endpoints (normal validation and audit).
- Facts the model must not invent are computed by the server: relevant lessons (lessons search, no model), at-risk milestones, dependent
  projects (RAID dependencies), weights normalised to exactly 100 %.
- Cost figures are sent to the model only when the caller may see costs (ADR 0098).
- MCP: `portfolio_project_backlog`, `portfolio_sprint_summary`, `portfolio_summary` (read, on); `portfolio_create_story_draft` (write, off).

## Consequences
The remaining SPEC §1.15 assists are complete; everything works with AI off.
