# 0074 — Project risks behind a scoring interface; project KPIs and the KPI catalogue

- **Status:** Proposed (Phase 9)
- **Context:** SPEC §1.4 D project risks (probability 1–5, impact 1–5, risk factor, inherent score, existing control evaluation 1–10, residual
  status, response, mitigation, implementation %). Q-P6: project risk scoring uses the shared Risk framework (§3.3) — built in Phase 13.
  Project KPIs are New or Existing (→ KPI card); goal status uses the KPI calculator with the RAG bands.

## Decision
- `IProjectRiskScoring` (Portfolio) computes inherent and residual scores and bands. Default implementation: inherent = probability × impact
  (1–25); residual = inherent × (11 − control) ÷ 10 (control 10 removes 90 %; no evaluation = inherent); bands **Low 1–4, Medium 5–9, High
  10–16, Critical 17–25**. Scores and band codes are **stored** on the risk with the inputs, so Phase 13 can register its framework-based
  implementation and recompute from the same inputs without a data migration.
- `IProjectRiskDirectory` (Portfolio.Contracts) lists project risks with scores / bands for a user (project read scope) — the Risk module's
  future *Project Risks* tab reads it.
- Project KPIs: **Existing** stores the KPI card id (picked through `IKpiCatalog.SearchAsync`, caller's scope) and shows the card's year
  performance (`GetPerformanceAsync`); **New** stores its own definition and current result, performance via `IKpiPerformance.Calculate` and
  band via `BandAsync`. Portfolio implements `IKpiLinkedRecordSource` so the card lists linked projects through the Links registry.

## Consequences
No Risk-module code in Portfolio; Phase 13 swaps the scoring service and reads risks through the contract.
