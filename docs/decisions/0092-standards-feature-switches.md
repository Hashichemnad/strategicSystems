# 0092 — Feature switches for the project / portfolio standards enhancements

- **Status:** Proposed (Phase 10)
- **Context:** SPEC §1.12: each enhancement "can be switched on/off per installation". Defaults are not stated.

## Decision
- Settings → Portfolio `features`: portfolios, prioritisation, health, baselines, earnedValue, raid, riskMatrix, raci, communicationPlan,
  benefits, quality, procurement, statusReport, gateChecklists, templates. **All on by default except earnedValue** (the SPEC and CLAUDE.md
  call EVM optional).
- Off = the UI hides it **and** its endpoints answer 404 `portfolio.errors.featureOff` (endpoint filter, so no handler runs); its gates,
  jobs, MCP tools and AI assists do nothing. Data is kept and reappears when switched on. `GET /portfolio/features` tells the SPA.
- `prioritisation` off means the four default criteria with equal weights and the default bands (Q-P6) are used; the editor is read-only.
- The capacity view and quarterly portfolio review records of §1.12 are not in the Phase 10 build-plan scope and are not built.

## Consequences
Every enhancement is testable on and off; the switch is a single source checked on the server.
