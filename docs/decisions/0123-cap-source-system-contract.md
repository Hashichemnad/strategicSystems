# 0123 — CAP source-system contract, live links and completion reporting; KPI and Risk integrations

- **Status:** Proposed (Phase 15)
- **Context:** SPEC §9.1 / §9.5: other systems (Benchmarking, Audit, KPI, Risk…) create remarks with a live link back and receive the
  completion. CLAUDE.md §5: modules call each other only through Contracts and integration events. ADR 0065 (`KpiTargetMissed`), Phase 13
  (`RiskEventLogged`, `RiskControlRatedIneffective`, `IRiskDirectory.LinkCapRemarkAsync`).

## Decision
- `Strategy.Modules.Cap.Contracts.ICapRemarkSources`: `EnsureSourceAsync` (one source per linked record, idempotent), `AddRemarkAsync`
  (idempotent by external key within the source; optional proposed actions created as draft actions), `GetProgressAsync` (live remarks
  and actions). An optional `transactionContext` joins the caller's transaction (ADR 0023).
- Integration events `CapRemarkStatusChanged` (every status change, with the source's and remark's linked records) and
  `CapActionCompleted`. `allActionsCompleted` = ≥ 1 completed action, every remark final and every approved action completed (completion
  requires evidence).
- KPI and Risk events create **suggestions** (`manual`, default — PMO raises in one click), raise remarks at once (`automatic`) or are
  ignored (`off`), per source in Settings → CAP; KPI misses only from `kpiMissConsecutive` red periods. Source per KPI card / risk, remark
  per result / event / control rating. After a risk-event remark CAP calls `LinkCapRemarkAsync`; the Risk events tab links to the remark.
- KPI cards and results gain the `remark-source` link capability (additive).

## Consequences
Audit Management (Phase 16) uses the same contract. Consumers never read CAP tables.
