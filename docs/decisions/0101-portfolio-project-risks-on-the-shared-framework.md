# 0101 — Portfolio project risks scored by the shared risk framework

- **Status:** Proposed (Phase 13) — awaiting owner acceptance
- **Context:** Q-P6 / SPEC §3.3: project risks use the shared framework so a project risk and an enterprise risk with the same ratings
  always score the same. ADR 0074 left `IProjectRiskScoring` in Portfolio with the Phase 9 default and stored scores with their inputs.
  Modules may only reference other modules' Contracts; Phase 10 (Portfolio Agile) is built in parallel, so Portfolio changes must stay small.

## Decision
- `Strategy.Modules.Risk.Contracts.IRiskScoring.ScoreProjectRiskAsync(probability, impact, controlEvaluation)` scores with the current
  framework: inherent = method(L, I, C = 0); residual per the framework's `project_residual_rule` — `control-reduction` (default:
  inherent × (11 − C) ÷ 10, the Phase 9 rule) or `formula` (the custom formula with `C`). Bands come from the framework.
- Portfolio references `Strategy.Modules.Risk.Contracts`; `IProjectRiskScoring` becomes async and its default implementation uses
  `IRiskScoring` when registered (Risk enabled), else the Phase 9 rule.
- Portfolio's `inherent_score` column stays an integer (no Portfolio migration while Phase 10 changes Portfolio in parallel): a fractional
  inherent score from a custom formula is stored rounded; the band is taken from the framework's exact score.
- Portfolio handles `RiskFrameworkChanged` (recalculation `now`, and the seeded version 1): it re-scores every active project risk through
  EF tracked changes, so the audit interceptor writes one entry per changed row. No data migration.

## Consequences
With the default framework the Phase 9 scores do not change (same rule and bands); a framework change re-rates project risks immediately
(project risks have no periodic review to wait for, so `next-review` re-rates them on their next edit).
