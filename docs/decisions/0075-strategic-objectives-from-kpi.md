# 0075 — Strategic objectives come from the KPI strategy hierarchy

- **Status:** Accepted by the owner (Phase 9)
- **Context:** Phase 8 seeded an empty Portfolio lookup `portfolio.strategic-objective` (Q-P7) and noted that the KPI system could supply
  objectives. Phase 12 built the strategy hierarchy (vision → perspective → objective) in the KPI module. Two lists of objectives would drift.

## Decision
- The KPI strategy hierarchy is the **single source** of strategic objectives. New contract `IKpiStrategyCatalog` (Kpi.Contracts):
  `ListObjectivesAsync()` (code, labels, active, sort) and `EnsureObjectivesAsync(items)` (creates missing objectives by code; never changes
  existing ones). Portfolio's options, request validation, AI form choices and project pages read objectives through it.
- Requests and projects keep storing the **code** (no data migration of business records).
- **Lookup migration:** an idempotent Portfolio seeder reads every `portfolio.strategic-objective` lookup value (active and inactive) and calls
  `EnsureObjectivesAsync` (same code, labels, sort, active flag; no parent). Then the lookup type is no longer registered by Portfolio (its
  values stay in the database for history). The development seed creates the demo objectives in the KPI hierarchy.
- When the KPI module is switched off, Portfolio falls back to an empty list (objective optional) — no hard dependency at runtime.

## Consequences
Settings → KPI → Strategy hierarchy maintains objectives for both systems.
