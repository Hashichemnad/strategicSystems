# 0103 — Project Risks tab: live, permission-filtered read; separate dashboard category

- **Status:** Accepted by the owner (Phase 13, before the pull request)
- **Context:** SPEC §0.0 / §3.4 G: read-only, live view of Portfolio project risks; each row opens the risk inside its project; included
  in heat-maps and dashboards as a separate filterable category; **never copied** into the enterprise register.

## Decision
- The Risk module reads `IProjectRiskDirectory.ListAsync(user, projectId, band)` (Portfolio.Contracts, ADR 0074) on every request; the
  caller's project read scope is applied by Portfolio. Nothing is stored in schema `risk`.
- Dashboards / heat-maps take `includeProjectRisks` (default from `RiskSettings`) and show project risks as category `project` (cells
  carry `enterprise` and `project` counts; clicking a cell lists both, project rows link to `/portfolio/projects/{id}?tab=risks`).
- Project risks have no residual likelihood / impact (only a 1–10 control evaluation), so they are placed on the **inherent** heat-map at
  their probability × impact cell; the residual heat-map shows enterprise risks only, while the "by residual rating" counts include project
  risks by their residual band.
- Project risks need `risk.project-risks.read` in the Risk system **and** project read access in Portfolio.

## Consequences
Project risk heat-map positions use the stored project scores, which Portfolio keeps current through ADR 0101.
