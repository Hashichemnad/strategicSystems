# 0140 — Reporting read model: module metric sources through Contracts, facts in the `reporting` schema

- **Status:** Accepted by the owner (Phase 17)
- **Context:** SPEC §0.4 asks for cross-system dashboards on cached aggregates ("refreshed by the scheduler and on relevant changes",
  executive load < 3 s). CLAUDE.md §5 forbids cross-schema SQL and references to other modules' internals. Today each module computes its
  dashboard live in its own handler; no Contract exposes aggregates.

## Decision
- New project `Strategy.Platform.Reporting.Contracts`: `IReportingMetricSource` (metric catalogue + `ComputeAsync` returning
  `MetricFact` rows + `ComputeForUserAsync` for the live path), `IReportingDatasetSource` (report-builder objects), `IMyWorkCardSource`.
- Each module implements them in a `Reporting/` folder, **querying only its own DbContext**. The reporting engine
  (`Strategy.Platform.Reporting`, new `ReportingDbContext`, schema `reporting`) stores the returned facts in `reporting.fact`
  (month grain, owning unit, partition, dimensions, additive components `n / num / den / w`).
- Facts are derived data: refresh runs are logged, rows are not entity-audited; the runtime role may SELECT / INSERT / DELETE them.
- Architecture tests: Reporting references no module; modules reference only the Reporting Contracts (+ the Platform helper).

## Consequences
One place for every dashboard figure; adding a system adds its sources and appears automatically in the builder. Module dashboards may
keep their live endpoints (Level 2) — a test asserts that a module's facts equal its live dashboard figures on the same data.
